using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Nichan
{
    // 時刻範囲判定と表示で同じ本文を共有し、有効日時のないログもキャッシュ
    sealed class PastThreadRepository : IDisposable
    {
        private readonly HttpClient client;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1);
        private readonly Dictionary<string, (Thread Thread, DateTime Expires)> cache = new Dictionary<string, (Thread, DateTime)>();
        public string LastError { get; private set; }
        public PastThreadRepository(string userAgent, HttpMessageHandler handler = null)
        {
            client = BoardClient.CreateHttpClient(handler);
            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
            }
        }
        public async Task<Thread> GetAsync(string url, CancellationToken token)
        {
            var address = ThreadAddress.Parse(url);
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (cache.TryGetValue(address.Identity, out var cached) && DateTime.UtcNow < cached.Expires)
                    return cached.Thread;
                Thread result = null;
                bool archived = false;
                var attempts = new List<(Uri Uri, bool Html, bool Archive)>();
                if (ThreadAddress.Is5chHost(address.BoardUri.Host))
                    attempts.Add((address.ArchiveDatUri, false, true));
                attempts.Add((address.DatUri, false, false));
                attempts.Add((new Uri(address.ThreadUri.AbsoluteUri.Replace("read.cgi/", "read.cgi/c/")), true, true));
                if (ThreadAddress.Is5chHost(address.BoardUri.Host))
                {
                    string server = address.BoardUri.Host.Split('.')[0];
                    attempts.Add((new Uri($"https://{server}.2ch.sc/{address.Board}/dat/{address.ThreadId}.dat"), false, true));
                }
                foreach (var attempt in attempts)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, attempt.Uri);
                        if (attempt.Html)
                            request.Headers.TryAddWithoutValidation("Cookie", "READJS=off; 5chClassic=on");
                        using var response = await client.SendAsync(request, token).ConfigureAwait(false);
                        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone || response.StatusCode == HttpStatusCode.Forbidden)
                            continue;
                        response.EnsureSuccessStatusCode();
                        var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                        if (attempt.Html)
                        {
                            using var reader = new StringReader(ArchivedThreadListRetriever.DecodeHtml(bytes, response.Content.Headers.ContentType?.CharSet));
                            result = ThreadParser.ParseFromStream(reader);
                        }
                        else
                        {
                            if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
                                throw new DatParserException("DATが空か途中で終了しています。");
                            var parser = new DatParser();
                            parser.Feed(BoardClient.DatEncoding.GetString(bytes));
                            result = new Thread { Title = parser.ThreadTitle };
                            Res res;
                            while ((res = parser.PopRes()) != null)
                                result.Res.Add(res);
                        }
                        result.Name = address.ThreadId;
                        result.Uri = address.ThreadUri;
                        result.ResCount = result.Res.Count;
                        archived = attempt.Archive;
                        LastError = null;
                        break;
                    }
                    catch (Exception e) when (e is HttpRequestException || e is NichanException || e is IOException || e is System.Xml.XmlException)
                    {
                        LastError = "一部の過去ログを取得できませんでした。";
                    }
                }
                token.ThrowIfCancellationRequested();
                if (result == null)
                    LastError = "取得できない過去ログがあります。";
                if (cache.Count >= 128)
                    cache.Remove(cache.Keys.First());
                cache[address.Identity] = (result, DateTime.UtcNow + (result == null ? TimeSpan.FromMinutes(5) : archived ? TimeSpan.FromDays(1) : TimeSpan.FromSeconds(30)));
                return result;
            }
            finally
            {
                gate.Release();
            }
        }
        internal static (DateTimeOffset First, DateTimeOffset Last)? GetTimeRange(Thread thread)
        {
            var times = thread?.Res.Where(r => r.Number <= 1000 && r.Date.HasValue)
                .Select(r => new DateTimeOffset(DateTime.SpecifyKind(r.Date.Value, DateTimeKind.Unspecified), TimeSpan.FromHours(9))).ToArray();
            return times == null || times.Length == 0 ? null : (times.Min(), times.Max());
        }
        public void Dispose() => client.Dispose();
    }
}
