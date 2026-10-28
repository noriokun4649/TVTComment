using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nichan
{
    public sealed class BoardClient
    {
        public static BoardClient Shared { get; } = new BoardClient();
        internal static Encoding DatEncoding { get; } = CreateEncoding();
        private static Encoding CreateEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932);
        }

        private readonly HttpClient client;
        private readonly ConcurrentDictionary<string, Uri> locations = new ConcurrentDictionary<string, Uri>();
        private readonly SemaphoreSlim menuLock = new SemaphoreSlim(1);
        private Dictionary<string, Uri> menu = new Dictionary<string, Uri>();
        private DateTime menuExpires;

        public BoardClient(HttpClient client = null)
        {
            this.client = client ?? CreateHttpClient();
        }

        internal static HttpClient CreateHttpClient(HttpMessageHandler handler = null)
        {
            var client = new HttpClient(handler ?? new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", TVTComment.Model.Utils.MiscUtils.GetUserAgent());
            return client;
        }
        public Uri GetKnownBoard(Uri uri)
        {
            uri = ThreadAddress.NormalizeBoard(uri);
            return locations.TryGetValue(uri.AbsoluteUri, out var result) ? result : uri;
        }
        internal void Remember(Uri original, Uri final)
        {
            original = ThreadAddress.NormalizeBoard(original);
            final = ThreadAddress.NormalizeBoard(final);
            locations[original.AbsoluteUri] = final;
        }
        public async Task<Uri> ResolveMovedBoardAsync(Uri board, CancellationToken token)
        {
            board = ThreadAddress.NormalizeBoard(board);
            if (!ThreadAddress.Is5chHost(board.Host))
                return board;
            await menuLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (DateTime.UtcNow >= menuExpires)
                {
                    using var response = await client.GetAsync("https://menu.5ch.io/bbsmenu.html", token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    string html = DatEncoding.GetString(await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false));
                    var updated = new Dictionary<string, Uri>();
                    foreach (Match match in Regex.Matches(html, @"href\s*=\s*[""']?(https?://[^\s""'<>]+)", RegexOptions.IgnoreCase))
                    {
                        if (!Uri.TryCreate(WebUtility.HtmlDecode(match.Groups[1].Value), UriKind.Absolute, out var uri) || !ThreadAddress.Is5chHost(uri.Host))
                            continue;
                        if (!Regex.IsMatch(uri.AbsolutePath, "^/[a-zA-Z0-9_]+/$"))
                            continue;
                        updated[uri.AbsolutePath] = ThreadAddress.NormalizeBoard(uri);
                    }
                    menu = updated;
                    menuExpires = DateTime.UtcNow.AddMinutes(30);
                }
                if (menu.TryGetValue(board.AbsolutePath, out var moved))
                {
                    Remember(board, moved);
                    return moved;
                }
                return board;
            }
            finally
            {
                menuLock.Release();
            }
        }

        public async Task<List<Thread>> GetThreadsAsync(Uri board, CancellationToken token = default)
        {
            var original = ThreadAddress.NormalizeBoard(board);
            board = GetKnownBoard(original);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var response = await client.GetAsync(new Uri(board, "subject.txt"), token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound && attempt == 0)
                {
                    board = await ResolveMovedBoardAsync(board, token).ConfigureAwait(false);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var final = response.RequestMessage?.RequestUri ?? new Uri(board, "subject.txt");
                board = ThreadAddress.NormalizeBoard(final);
                Remember(original, board);
                var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                using var reader = new StringReader(DatEncoding.GetString(bytes));
                var threads = await SubjecttxtParser.ParseFromStream(reader).ToListAsync(token).ConfigureAwait(false);
                threads.RemoveAll(thread => new ThreadAddress(board, thread.Name).IsAnnouncement);
                foreach (var thread in threads)
                    thread.Uri = new ThreadAddress(board, thread.Name).ThreadUri;
                return threads;
            }
            throw new HttpRequestException("板が見つかりません。", null, HttpStatusCode.NotFound);
        }
    }
}
