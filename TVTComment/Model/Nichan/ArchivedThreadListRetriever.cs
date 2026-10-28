using Sgml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Nichan
{
    sealed class ArchivedThreadListRetriever
    {
        public string Board => board.AbsolutePath.Trim('/');
        public bool IsPrepared => root != null;
        private readonly Uri board;
        private readonly HttpClient client;
        private Page root;
        private readonly Dictionary<string, (Page Page, DateTime Expires)> cache = new Dictionary<string, (Page, DateTime)>();
        internal sealed class Page
        {
            public Uri Uri;
            public List<Thread> Threads = new List<Thread>();
            public List<(Uri Uri, long Start, long End)> Ranges = new List<(Uri, long, long)>();
            public List<Uri> Servers = new List<Uri>();
            public int LastPage = 1;
        }
        public ArchivedThreadListRetriever(string board, string server) : this(new Uri($"https://{server}.5ch.io/{board}/"))
        {
        }

        public ArchivedThreadListRetriever(Uri board, HttpClient client = null)
        {
            this.board = ThreadAddress.NormalizeBoard(board);
            this.client = client ?? sharedClient;
        }

        private static readonly HttpClient sharedClient = BoardClient.CreateHttpClient();

        public async Task Prepare(CancellationToken token)
        {
            root = await GetPageAsync(new Uri(board, "kako/"), token).ConfigureAwait(false);
        }

        internal static Page ParsePage(string html, Uri finalUri)
        {
            using var reader = new StringReader(html);
            using var sgml = new SgmlReader { DocType = "HTML", IgnoreDtd = true, CaseFolding = CaseFolding.ToLower, InputStream = reader };
            var document = XDocument.Load(sgml);
            var page = new Page { Uri = finalUri };
            var byId = new Dictionary<string, Thread>();
            foreach (var row in document.Descendants().Where(e => HasClass(e, "main_odd") || HasClass(e, "main_even")))
            {
                var link = row.Descendants().FirstOrDefault(x => HasClass(x, "title"))?.Descendants("a").FirstOrDefault();
                var href = (string)link?.Attribute("href");
                if (href == null || !ThreadAddress.TryParse(new Uri(finalUri, href).AbsoluteUri, out var address))
                    continue;
                int.TryParse(row.Descendants().FirstOrDefault(x => HasClass(x, "lines"))?.Value.Trim(), out int count);
                byId[address.Identity] = new Thread
                {
                    Uri = address.ThreadUri,
                    Name = address.ThreadId,
                    Title = link.Value.Trim(),
                    ResCount = count
                };
            }
            // 旧サーバー集約サイトのサーバー描画済み一覧にも対応
            foreach (var link in document.Descendants("a"))
            {
                var href = (string)link.Attribute("href");
                if (href == null || !Uri.TryCreate(finalUri, href, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                    continue;
                uri = ThreadAddress.Normalize(uri);
                if (ThreadAddress.TryParse(uri.AbsoluteUri, out var address))
                {
                    if (!byId.ContainsKey(address.Identity))
                        byId[address.Identity] = new Thread
                        {
                            Uri = address.ThreadUri,
                            Name = address.ThreadId,
                            Title = link.Value.Trim()
                        };
                    continue;
                }
                var range = Regex.Match(link.Value, @"(?<first>\d{9,12})\s*-\s*(?<last>\d{9,12})");
                if (range.Success && uri.AbsolutePath.Contains("/kako/", StringComparison.Ordinal))
                {
                    long a = long.Parse(range.Groups["first"].Value), b = long.Parse(range.Groups["last"].Value);
                    page.Ranges.Add((uri, Math.Min(a, b), Math.Max(a, b)));
                }
                else if (HasClass(link.Parent, "menu_link") && uri.Host != finalUri.Host && ThreadAddress.Is5chHost(uri.Host))
                    page.Servers.Add(uri);
                var pagination = Regex.Match(uri.Query, @"(?:\?|&)page=(\d+)(?:&|$)");
                if (uri.Host == finalUri.Host && pagination.Success && int.TryParse(pagination.Groups[1].Value, out var number))
                    page.LastPage = Math.Max(page.LastPage, number);
            }
            page.Threads = byId.Values.ToList();
            return page;
        }
        private static bool HasClass(XElement element, string name) => ((string)element?.Attribute("class") ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Contains(name);

        private async Task<Page> GetPageAsync(Uri uri, CancellationToken token)
        {
            if (cache.TryGetValue(uri.AbsoluteUri, out var cached) && DateTime.UtcNow < cached.Expires)
                return cached.Page;
            using var response = await client.GetAsync(uri, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            var html = DecodeHtml(bytes, response.Content.Headers.ContentType?.CharSet);
            var page = ParsePage(html, response.RequestMessage?.RequestUri ?? uri);
            if (cache.Count >= 64)
                cache.Remove(cache.Keys.First());
            // kako0001等も先頭ページの増加で内容が移動するため、永久保存しない
            cache[uri.AbsoluteUri] = (page, DateTime.UtcNow.AddMinutes(5));
            return page;
        }
        internal static string DecodeHtml(byte[] bytes, string charset)
        {
            var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            bool sjis = (charset ?? header).Contains("shift", StringComparison.OrdinalIgnoreCase) || (charset ?? "").Contains("932");
            return (sjis ? BoardClient.DatEncoding : Encoding.UTF8).GetString(bytes);
        }

        public async Task<IEnumerable<Thread>> GetThreadsCreatedAt(DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken token)
        {
            if (startTime > endTime)
                throw new ArgumentException("検索時刻の範囲が不正です。");
            await Prepare(token).ConfigureAwait(false);
            long start = startTime.ToUnixTimeSeconds(), end = endTime.ToUnixTimeSeconds();
            var found = new List<Thread>();
            var pending = new Queue<Page>();
            pending.Enqueue(root);
            var visited = new HashSet<string>();
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var page = pending.Dequeue();
                if (!visited.Add(page.Uri.AbsoluteUri))
                    continue;
                if (visited.Count > 128)
                    throw new InvalidDataException("検索対象の過去ログ一覧が多すぎます。検索範囲を短くしてください。");
                found.AddRange(page.Threads.Where(t => long.TryParse(t.Name, out var id) && start <= id && id <= end));
                foreach (var range in page.Ranges.Where(r => r.Start <= end && start <= r.End))
                {
                    if (visited.Contains(range.Uri.AbsoluteUri))
                        continue;
                    try
                    {
                        pending.Enqueue(await GetPageAsync(range.Uri, token).ConfigureAwait(false));
                    }
                    catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
                    { }
                }
                long oldest = page.Threads.Select(t => long.Parse(t.Name)).DefaultIfEmpty(long.MaxValue).Min();
                long oldestRange = page.Ranges.Select(r => r.Start).DefaultIfEmpty(oldest).Min();
                if (start < Math.Min(oldest, oldestRange))
                {
                    foreach (var server in page.Servers)
                    {
                        try
                        {
                            var other = await GetPageAsync(server, token).ConfigureAwait(false);
                            if (other.LastPage > 1 && other.Ranges.Count == 0)
                                found.AddRange(await GetPagedThreadsAsync(other, start, end, token).ConfigureAwait(false));
                            else
                                pending.Enqueue(other);
                        }
                        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
                        { }
                    }
                }
            }
            return found.DistinctBy(t => ThreadAddress.Parse(t.Uri.AbsoluteUri).Identity).ToArray();
        }

        // 集約一覧はスレッド番号昇順のため、全ページ巡回せず境界を二分探索
        private async Task<IEnumerable<Thread>> GetPagedThreadsAsync(Page first, long start, long end, CancellationToken token)
        {
            int low = 1, high = first.LastPage;
            Uri PageUri(int index) => new UriBuilder(first.Uri) { Query = "page=" + index }.Uri;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                var page = await GetPageAsync(PageUri(middle), token).ConfigureAwait(false);
                if (page.Threads.Count == 0)
                    return Array.Empty<Thread>();
                if (page.Threads.Max(t => long.Parse(t.Name)) < start)
                    low = middle + 1;
                else
                    high = middle;
            }
            var result = new List<Thread>();
            for (int index = low; index <= first.LastPage; index++)
            {
                if (index - low >= 64)
                    throw new InvalidDataException("過去ログの検索範囲が広すぎます。");
                var page = await GetPageAsync(PageUri(index), token).ConfigureAwait(false);
                result.AddRange(page.Threads.Where(t => start <= long.Parse(t.Name) && long.Parse(t.Name) <= end));
                if (page.Threads.Count == 0 || page.Threads.Max(t => long.Parse(t.Name)) >= end)
                    break;
            }
            return result;
        }
    }
}
