using Sgml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Nichan
{
    class ArchivedThreadListRetriever
    {
        public string Board { get; }
        public bool IsPrepared => rangeAndUrls != null;

        public ArchivedThreadListRetriever(string board, string oneOfTheServer)
        {
            Board = board;
            server = oneOfTheServer;
        }

        public async Task Prepare(CancellationToken cancellationToken)
        {
            string mainDocumentUrl = $"https://{server}.5ch.net/{Board}/kako/";
            var startTimeToUrlMapping = new SortedList<long, IEnumerable<(string url, (long start, long end) range)>>();

            static (long start, long end) getThreadRange(string str)
            {
                var strs = str.Split('-');
                if (strs.Length != 2)
                    return (0, 0);
                var nums = strs.Select(x => long.TryParse(x.Trim(), out long num) ? num : 0).ToArray();
                if (nums.Any(x => x == 0))
                    return (0, 0);
                return (nums[1], nums[0]);
            }

            var otherServerDocument = await GetHtml(mainDocumentUrl, cancellationToken).ConfigureAwait(false);
            var urlAndRanges = new List<(string url, (long start, long end) range)>
                {
                    (
                    "",
                    getThreadRange(((XText)otherServerDocument.XPathSelectElement(
                        @"//*[contains(@class, ""menu"")]/*[contains(@class, ""menu_here"")]"
                    ).Nodes().First(x => x.NodeType == XmlNodeType.Text)).Value)
                )
                };
            urlAndRanges.AddRange(otherServerDocument.XPathSelectElements(
                    @"//*[contains(@class, ""menu"")]/*"
                ).SkipWhile(x => x.Name != "h2").Skip(1).TakeWhile(x => x.Name != "h2").Where(
                    x => x.Attribute("class")?.Value == "menu_link"
                ).Select(x => x.Element("a")).Select(x => (x.Attribute("href").Value, getThreadRange(x.Value)))
            );
            foreach (var x in urlAndRanges)
            {
                var url = new Uri(new Uri(mainDocumentUrl), x.url).ToString();
                if (!startTimeToUrlMapping.TryGetValue(x.range.start, out var value))
                {
                    value = new List<(string url, (long start, long end) range)>();
                    startTimeToUrlMapping.Add(x.range.start, value);
                }
                ((List<(string, (long, long))>)value).Add((url, x.range));
            }

            rangeAndUrls = startTimeToUrlMapping;
        }

        /// <summary>
        /// 指定した時間範囲に作られたスレッドのリストを返す。
        /// 返り値の<see cref="Thread"/>のインスタンスはキャッシュされて過去の呼び出し時と同じものが返るので注意。
        /// </summary>
        public async Task<IEnumerable<Thread>> GetThreadsCreatedAt(
            DateTimeOffset startTime, DateTimeOffset endTime,
            CancellationToken cancellationToken
        )
        {
            if (startTime > endTime)
                throw new ArgumentException(@$"""{nameof(startTime)}"" must be earlier than ""{nameof(endTime)}""");
            if (!IsPrepared)
                throw new InvalidOperationException(@$"""{nameof(Prepare)}"" must be called beforehand");

            long start = startTime.ToUnixTimeSeconds();
            int startUrlIdx = Array.BinarySearch(rangeAndUrls.Keys.ToArray(), start);
            if (startUrlIdx < 0)
            {
                startUrlIdx = ~startUrlIdx - 1;
                if (startUrlIdx == -1)
                    startUrlIdx = 0;
            }

            long end = endTime.ToUnixTimeSeconds();
            int endUrlIdx = Array.BinarySearch(rangeAndUrls.Keys.ToArray(), end);
            if (endUrlIdx < 0)
            {
                endUrlIdx = ~endUrlIdx;
            }

            var urlList = rangeAndUrls.Values.ToArray()[startUrlIdx..endUrlIdx].SelectMany(x => x.Select(x => x.url));
            var ret = new List<(long threadId, Thread thread)>(); // スレの作成時間とスレのリスト
            foreach (string url in urlList)
            {
                // キャッシュになければ取得しにいく
                if (!threadListCache.TryGetValue(url, out IEnumerable<(long threadId, Thread thread)> threads))
                {
                    var threadList = new List<(long threadId, Thread thread)>();
                    XDocument doc = await GetHtml(url, cancellationToken).ConfigureAwait(false);
                    foreach (var elem in doc.XPathSelectElements(
                        @"//*[contains(@class, ""main"")]/*[contains(@class, ""main_odd"") or contains(@class, ""main_even"")]"
                    ))
                    {
                        XElement filenameElem = elem.XPathSelectElement(@"./*[contains(@class, ""filename"")]");
                        string filename = filenameElem?.Value;
                        long threadId = long.Parse(filename[0..^4]);

                        XElement anchorElem = elem.XPathSelectElement(@"./*[contains(@class, ""title"")]/a");
                        string threadUrl = anchorElem?.Attribute("href")?.Value ?? "";
                        string threadTitle = anchorElem?.Value ?? "";
                        XElement linesElem = elem.XPathSelectElement(@"./*[contains(@class, ""lines"")]");
                        int resCount = int.Parse(linesElem?.Value ?? "0");

                        if (threadUrl == "" || threadTitle == "" || resCount == 0)
                            continue;

                        threadList.Add((threadId, new Thread()
                        {
                            Uri = new Uri(new Uri(url), threadUrl),
                            Name = threadId.ToString(),
                            Title = threadTitle,
                            ResCount = resCount,
                        }));
                    }

                    threadListCache.Add(url, threadList);

                    threads = threadList;
                }

                ret.AddRange(threads.Where(x => start <= x.threadId && x.threadId < end));
            }

            // 複数のサーバーに同じスレッドが保存されてることがあるので
            return ret.Distinct(new KeyEqualityComparer<(long, Thread), long>(x => x.Item1)).Select(x => x.Item2);
        }

        private static readonly HttpClient httpClient = new HttpClient();

        private readonly string server;
        private SortedList<long, IEnumerable<(string url, (long start, long end) range)>> rangeAndUrls = null;
        /// <summary>
        /// キーが https://{server}.5ch.net/{board}/kako/kako****.html の形のURL、値がそのページのスレッドIDとスレッドのリスト
        /// </summary>
        private readonly Dictionary<string, IEnumerable<(long, Thread)>> threadListCache = new Dictionary<string, IEnumerable<(long, Thread)>>();

        private static async Task<XDocument> GetHtml(string url, CancellationToken cancellationToken)
        {
            System.Diagnostics.Debug.WriteLine($"[ArchivedThreadListRetriever] HTTP Get {url}");
            string response;
            try
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "READJS=\"off\"; 5chClassic=on");
                response = await httpClient.GetStringAsync(url, cancellationToken);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == null)
                    throw new NetworkException(url, null, e);
                else
                    throw new HttpErrorResponseException((int)e.StatusCode.Value, null, url, null, e);
            }

            using var textReader = new StringReader(response);
            using var sgml = new SgmlReader { DocType = "HTML", IgnoreDtd = false, InputStream = textReader };
            sgml.WhitespaceHandling = WhitespaceHandling.None;
            sgml.CaseFolding = CaseFolding.ToLower;
            // XDocument.LoadAsyncはNotImplementedを投げてくる
            //doc = await XDocument.LoadAsync(sgml, LoadOptions.None, cancellationToken);
            XDocument doc = XDocument.Load(sgml);

            return doc;
        }

        private class KeyEqualityComparer<T, TKey> : IComparer<T>, IEqualityComparer<T>
        {
            private readonly Func<T, TKey> keyExtractor;
            private readonly IComparer<TKey> comparer;
            private readonly IEqualityComparer<TKey> equalityComparer;

            public KeyEqualityComparer(Func<T, TKey> keyExtractor)
            {
                this.keyExtractor = keyExtractor;
                comparer = Comparer<TKey>.Default;
                equalityComparer = EqualityComparer<TKey>.Default;
            }

            public int Compare(T x, T y)
            {
                return comparer.Compare(keyExtractor(x), keyExtractor(y));
            }

            public bool Equals(T x, T y)
            {
                return equalityComparer.Equals(keyExtractor(x), keyExtractor(y));
            }

            public int GetHashCode(T obj)
            {
                return equalityComparer.GetHashCode(keyExtractor(obj));
            }
        }
    }
}
