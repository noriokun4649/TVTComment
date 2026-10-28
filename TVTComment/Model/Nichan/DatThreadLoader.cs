using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Nichan
{
    public class DatThreadLoaderException : NichanException
    {
        public DatThreadLoaderException() { }
        public DatThreadLoaderException(string message) : base(message) { }
        public DatThreadLoaderException(string message, Exception inner) : base(message, inner) { }
    }
    public class DatFormatDatThreadLoaderException : DatThreadLoaderException
    {
        public string DatString { get; }

        public DatFormatDatThreadLoaderException(string datString, Exception inner = null) : base("取得したDATの形式が不正です。", inner)
        {
            DatString = datString;
        }
    }
    public sealed class DatThreadLoader
    {
        private ThreadAddress address;
        public string ServiceProvider => string.Join('.', address.BoardUri.Host.Split('.').Skip(1));
        public string ServerName => address.BoardUri.Host.Split('.')[0];
        public string BoardName => address.Board;
        public string ThreadId => address.ThreadId;
        public Thread Thread { get; } = new Thread();
        public bool LastUpdated { get; private set; }
        public bool LastAllUpdated { get; private set; }
        private byte[] bytes = Array.Empty<byte>();
        private string lastModified;
        public DatThreadLoader(string provider, string server, string board, string threadId)
            : this(new ThreadAddress(new Uri($"https://{server}.{provider}/{board}/"), threadId))
        {
        }

        public DatThreadLoader(ThreadAddress address)
        {
            this.address = address;
            Thread.Name = address.ThreadId;
            Thread.Uri = address.ThreadUri;
        }

        public async Task Update(ApiClient client, CancellationToken token = default)
        {
            LastUpdated = LastAllUpdated = false;
            bool full = bytes.Length == 0;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var headers = new List<(string, string)>();
                if (!full)
                {
                    headers.Add(("Range", $"bytes={bytes.Length - 1}-"));
                    if (lastModified != null)
                        headers.Add(("If-Modified-Since", lastModified));
                }
                using var response = await client.GetDatResponse(address, headers, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotModified && !full)
                    return;
                if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
                {
                    // 404自体を再試行せず、板移転が確認できた場合だけ新URLを一度試す
                    if (attempt == 0 && ThreadAddress.Is5chHost(address.BoardUri.Host))
                    {
                        var moved = await BoardClient.Shared.ResolveMovedBoardAsync(address.BoardUri, token).ConfigureAwait(false);
                        if (moved != address.BoardUri)
                        {
                            address = new ThreadAddress(moved, ThreadId);
                            full = true;
                            continue;
                        }
                    }
                    throw new ThreadUnavailableException(response.StatusCode);
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    throw new AuthorizationApiClientException(response.StatusCode);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable || (response.StatusCode == HttpStatusCode.NotImplemented && !full))
                {
                    if (attempt == 0)
                    {
                        full = true;
                        continue;
                    }
                    throw new ResponseApiClientException(response.StatusCode);
                }
                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                    throw new ResponseApiClientException(response.StatusCode, ApiClient.GetRetryAfter(response));
                if (response.Content.Headers.ContentEncoding.Count > 0)
                    throw new DatFormatDatThreadLoaderException("圧縮された差分DATには対応していません。");
                var body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                bool all = response.StatusCode == HttpStatusCode.OK;
                if (!all)
                {
                    var range = response.Content.Headers.ContentRange;
                    bool valid = !full && body.Length > 0 && body[0] == (byte)'\n' && range?.Unit == "bytes"
                        && range.From == bytes.Length - 1 && range.To == range.From + body.Length - 1
                        && (!range.Length.HasValue || range.To < range.Length);
                    if (!valid)
                    {
                        if (attempt == 0)
                        {
                            full = true;
                            continue;
                        }
                        throw new DatFormatDatThreadLoaderException("Content-Rangeが不正です。");
                    }
                }
                byte[] candidate = all ? body : bytes.Concat(body.Skip(1)).ToArray();
                if (candidate.Length > 0 && candidate[^1] != (byte)'\n')
                    throw new DatFormatDatThreadLoaderException("DATが行の途中で終了しました。");
                string text = BoardClient.DatEncoding.GetString(candidate);
                var parser = new DatParser();
                var parsed = new List<Res>();
                try
                {
                    parser.Feed(text);
                    Res res;
                    while ((res = parser.PopRes()) != null)
                        parsed.Add(res);
                }
                catch (Exception e) when (e is DatParserException || e is System.Xml.XmlException)
                {
                    throw new DatFormatDatThreadLoaderException("", e);
                }
                token.ThrowIfCancellationRequested();
                // 成功時にまとめて適用し、解析失敗で差分位置を進めない
                LastUpdated = !bytes.SequenceEqual(candidate);
                LastAllUpdated = all;
                bytes = candidate;
                lastModified = response.Content.Headers.LastModified?.ToString("R");
                Thread.Title = parser.ThreadTitle;
                Thread.Res.Clear();
                Thread.Res.AddRange(parsed);
                Thread.ResCount = parsed.Count;
                var final = response.RequestMessage?.RequestUri;
                if (final != null && final.AbsolutePath.EndsWith($"/dat/{ThreadId}.dat", StringComparison.Ordinal))
                {
                    var finalBoard = ThreadAddress.NormalizeBoard(final);
                    BoardClient.Shared.Remember(address.BoardUri, finalBoard);
                    address = new ThreadAddress(finalBoard, ThreadId);
                }
                Thread.Uri = address.ThreadUri;
                return;
            }
        }
    }
}
