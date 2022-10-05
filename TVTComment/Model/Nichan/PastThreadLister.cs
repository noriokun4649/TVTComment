using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nichan
{
    /// <summary>
    /// 指定した板で、ある時刻、または時間範囲に存在したスレッドをリストする。
    /// ここでの「ある時刻に存在した」とは最初のレスの時刻から最期のレスの時刻までの間にその時刻が含まれているということ。
    /// </summary>
    class PastThreadLister
    {
        public string Board => threadListRetriever.Board;

        /// <param name="board">板</param>
        /// <param name="oneOfTheServer">板が所属したことがあるサーバーのうちの一つ</param>
        /// <param name="backTime">この値だけ過去に戻った時刻以降に作られたスレから検索する。過去スレ一覧は作成時間からしか取得できないため。</param>
        public PastThreadLister(string board, string oneOfTheServer, TimeSpan backTime, string pastUserAgent)
        {
            if (backTime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(backTime), $"{nameof(backTime)} must be positive");
            this.backTime = backTime;
            httpClient.DefaultRequestHeaders.Add("User-Agent", pastUserAgent);
            threadListRetriever = new ArchivedThreadListRetriever(board, oneOfTheServer);
        }

        public async Task Initialize(CancellationToken cancellationToken)
        {
            await threadListRetriever.Prepare(cancellationToken).ConfigureAwait(false);
        }

        public Task<IEnumerable<Thread>> GetAt(DateTimeOffset time, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 指定した時間範囲に存在したスレッドのリストを返す。 
        /// 返り値の<see cref="Thread"/>のインスタンスはキャッシュされて過去の呼び出し時と同じものが返るので注意。
        /// </summary>
        public async IAsyncEnumerable<Thread> GetBetween(
            DateTimeOffset startTime,
            DateTimeOffset endTime,
            string[] keywords,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var start = startTime - backTime;
            var threads = (await threadListRetriever.GetThreadsCreatedAt(start, endTime, cancellationToken).ConfigureAwait(false))
                .Where(x => keywords.Length == 0 || keywords.Any(keyword => x.Title.ToLower().Normalize(NormalizationForm.FormKD).Contains(keyword)));

            foreach (var thread in threads)
            {
                if (!threadTimeRangeCache.TryGetValue(thread.Uri.ToString(), out var range))
                {
                    try {
                        range = await GetThreadTimeRange(thread.Uri.ToString(), cancellationToken).ConfigureAwait(false);
                        threadTimeRangeCache.Add(thread.Uri.ToString(), range);
                    }
                    catch (HttpRequestException)
                    {
                        continue;
                    }
                }

                if (startTime <= range.lastResTime && range.firstResTime < endTime)
                {
                    yield return thread;
                }
            }
        }

        private static readonly HttpClient httpClient = new HttpClient();
        private readonly ArchivedThreadListRetriever threadListRetriever;
        private readonly TimeSpan backTime;
        /// <summary>
        /// キーがスレッドのURL、値がそのスレッドの最初と最後のレスの時刻
        /// </summary>
        private readonly Dictionary<string, (DateTimeOffset firstResTime, DateTimeOffset lastResTime)> threadTimeRangeCache = new Dictionary<string, (DateTimeOffset, DateTimeOffset)>();

        private static readonly Regex reThreadUrl = new Regex(@"//(?<server>[^.]*)\.\dch\.(sc|net)/test/read\.cgi/(?<board>[^/]*)/(?<thread>[^/]*)($|/)");
        private static async Task<(DateTimeOffset firstResTime, DateTimeOffset lastResTime)> GetThreadTimeRange(string threadUrl, CancellationToken cancellationToken)
        {
            var reses = new List<Res>();

            Match match = reThreadUrl.Match(threadUrl);
            if (match.Success)
            {
                // まず2ch.scのdatから取得する
                (string server, string board, string threadId) = (match.Groups["server"].Value, match.Groups["board"].Value, match.Groups["thread"].Value);
                string datUrl = $"http://{server}.2ch.sc/{board}/dat/{threadId}.dat";
                string datResponse = null;

                System.Diagnostics.Debug.WriteLine($"[PastThreadLister] HTTP Get {datUrl}");

                try
                {
                    datResponse = await httpClient.GetStringAsync(datUrl, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                }

                if (datResponse != null)
                {
                    var datParser = new DatParser();
                    datParser.Feed(datResponse);

                    while (true)
                    {
                        Res res = datParser.PopRes();
                        if (res == null) break;
                        reses.Add(res);
                    }
                }
            }

            if (reses.Count == 0)
            {
                // 2ch.scがダメだった場合、5ch.netのスクレイピング
                if (!threadUrl.EndsWith('/'))
                    threadUrl += "/";
                threadUrl += "l3";

                string response;

                System.Diagnostics.Debug.WriteLine($"[PastThreadLister] HTTP Get {threadUrl}");

                try
                {
                    response = await httpClient.GetStringAsync(threadUrl, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    if (e.StatusCode == null)
                        throw new NetworkException(threadUrl, null, e);
                    else
                        throw new HttpErrorResponseException((int)e.StatusCode.Value, null, threadUrl, null, e);
                }

                using var textReader = new StringReader(response);
                Thread thread = ThreadParser.ParseFromStream(textReader);
                reses = thread.Res;
            }

            var firstRes = reses[0];
            var lastRes = reses.FindLast(x => x.Number <= 1000 && x.Date != null);
            var start = firstRes.Date == null ? null : (DateTimeOffset?)new DateTimeOffset(firstRes.Date.Value, TimeSpan.FromHours(9));
            var end = lastRes.Date == null ? null : (DateTimeOffset?)new DateTimeOffset(lastRes.Date.Value, TimeSpan.FromHours(9));

            return (start.Value, end.Value);
        }
    }
}
