using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model.ChatCollectService
{
    class PastNichanChatCollectService : OnceASecondChatCollectService
    {
        public override string Name => "2ch過去ログ";

        public override string GetInformationText()
        {
            string ret = "";
            lock (threadList)
            {
                string threadInformation = string.Join("\n", threadList.Where(
                    x => currentThreadUrls.Contains(x.Uri.ToString())
                ).Select(x => $"{x.Title}  ({x.ResCount})  {x.Uri}"));
                if (threadInformation != "")
                    ret += threadInformation;
                else
                    ret += "[スレなし]";
            }
            return ret;
        }

        public override ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }

        public PastNichanChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry chatCollectServiceEntry,
            NichanUtils.INichanThreadSelector threadSelector,
            TimeSpan threadSelectionUpdateInterval
        ) : base(TimeSpan.FromSeconds(10))
        {
            ServiceEntry = chatCollectServiceEntry;
            this.threadSelector = threadSelector;
            this.threadSelectionUpdateInterval = threadSelectionUpdateInterval;

            resCollectLoopTask = Task.Run(() => ResCollectLoop(resCollectLoopTaskCancellation.Token));
        }

        protected override IEnumerable<Chat> GetOnceASecond(ChannelInfo channel, DateTime time)
        {
            lastGetChannel = channel;
            lastGetTime = new DateTimeOffset(time, TimeSpan.FromHours(9));

            if (resCollectLoopTask.IsCompleted)
            {
                try
                {
                    resCollectLoopTask.Wait();
                }
                catch (AggregateException e)
                {
                    ResCollectLoopTaskExceptionHandler(e);
                    return Enumerable.Empty<Chat>();
                }
            }

            bool leaped = time < this.lastTime || time > this.lastTime + continuousCallLimit;
            var lastTime = !leaped ? this.lastTime : time - TimeSpan.FromSeconds(1);
            // TODO: 録画の再生を一時停止すると時刻が数秒巻き戻ることがある。するとコメントが2重で表示される。
            lock (threadList)
            {
                return threadList.Where(
                    x => currentThreadUrls.Contains(x.Uri.ToString())
                ).SelectMany(x => x.Res).Where(
                    x => lastTime <= x.Date && x.Date < time
                ).Select(x =>
                {
                    foreach (var elem in x.Text.Descendants("br").ToArray())
                    {
                        elem.ReplaceWith("\n");
                    }

                    return new Chat(
                        x.Date.Value, x.Text.Value, Chat.PositionType.Normal, Chat.SizeType.Normal,
                        Color.White, x.UserId, x.Number
                    );
                });
            }
        }

        public override void Dispose()
        {
            resCollectLoopTaskCancellation.Cancel();
            try
            {
                resCollectLoopTask.Wait();
            }
            catch (AggregateException e)
            {
                try
                {
                    ResCollectLoopTaskExceptionHandler(e);
                }
                catch (ChatCollectException)
                { }
            }
        }

        private async Task ResCollectLoop(CancellationToken cancellationToken)
        {
            while (true)
            {
                if (lastGetChannel == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                IEnumerable<string> existingThreadUrls;
                lock (threadList)
                    existingThreadUrls = threadList.Select(x => x.Uri.ToString()).ToArray();

                var urls = new List<string>();
                await foreach (var url in threadSelector.Get(lastGetChannel, lastGetTime, cancellationToken))
                {
                    if (!existingThreadUrls.Contains(url))
                    {
                        var (server, board_, threadId) = GetServerBoardThreadFromThreadUrl(url);
                        var thread = await GetThread(server, board_, threadId, cancellationToken).ConfigureAwait(false);
                        thread.Uri = new Uri(url); // キャッシュにヒットするようにthreadSelectorの返したUriで記憶する

                        lock (threadList)
                            threadList.Add(thread);
                    }

                    urls.Add(url);
                    currentThreadUrls = urls.ToArray();
                }

                await Task.Delay(threadSelectionUpdateInterval, cancellationToken);
            }
        }

        private static readonly Regex reThreadUrl = new Regex(@"//(?<server>[^.]*)\.\dch\.(net|sc)/test/read\.cgi/(?<board>[^/]*)/(?<thread>\d*)($|/)");
        private static (string server, string board, string thread) GetServerBoardThreadFromThreadUrl(string threadUrl)
        {
            Match match = reThreadUrl.Match(threadUrl);
            return (match.Groups["server"].Value, match.Groups["board"].Value, match.Groups["thread"].Value);
        }

        private static async Task<Nichan.Thread> GetThread(string server, string board, string thread, CancellationToken cancellationToken)
        {
            Nichan.Thread ret;
            // まず2ch.scのdatから取得する
            string datUrl = $"http://{server}.2ch.sc/{board}/dat/{thread}.dat";
            string datResponse = null;

            System.Diagnostics.Debug.WriteLine($"[PastNichanChatCollectService] HTTP Get {datUrl}");

            try
            {
                datResponse = await httpClient.GetStringAsync(datUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
            }

            if (datResponse != null)
            {
                ret = new Nichan.Thread() { Name = thread };
                var datParser = new Nichan.DatParser();
                datParser.Feed(datResponse);

                ret.Uri = new Uri(datUrl);
                ret.Title = datParser.ThreadTitle;
                while (true)
                {
                    Nichan.Res res = datParser.PopRes();
                    if (res == null) break;
                    ret.Res.Add(res);
                }
                ret.ResCount = ret.Res.Count;
                return ret;
            }

            // 2ch.scがダメだった場合、5ch.netのスクレイピング
            string gochanUrl = $"http://{server}.5ch.net/test/read.cgi/{board}/{thread}/";
            string response;

            System.Diagnostics.Debug.WriteLine($"[PastNichanChatCollectService] HTTP Get {gochanUrl}");

            try
            {
                response = await httpClient.GetStringAsync(gochanUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == null)
                    throw new Nichan.NetworkException(gochanUrl, null, e);
                else
                    throw new Nichan.HttpErrorResponseException((int)e.StatusCode.Value, null, gochanUrl, null, e);
            }

            using var textReader = new StringReader(response);
            ret = Nichan.ThreadParser.ParseFromStream(textReader);
            ret.Uri = new Uri(gochanUrl);
            return ret;
        }

        /// <summary>
        /// <see cref="resCollectLoopTask"/>を<see cref="Task.Wait"/>した時に出た<see cref="AggregateException"/>を適切な例外に変換して投げなおす。
        /// 例外を投げなければタスクがキャンセルされたことによる例外だったということ。
        /// </summary>
        private static void ResCollectLoopTaskExceptionHandler(AggregateException e)
        {
            if (e.InnerExceptions.All(x => x is OperationCanceledException))
                return;
            if (e.InnerExceptions.Count != 1)
                ExceptionDispatchInfo.Capture(e).Throw(); // スタックトレースを保って再スロー

            switch (e.InnerExceptions[0])
            {
                case Nichan.HttpErrorResponseException http:
                    throw new ChatCollectException($"HTTP でエラーのステータスコードが返りました\nコード: {http.HttpStatusCode}\nURL: {http.Url}", e);
                case Nichan.ErrorResponseException errorResponse:
                    throw new ChatCollectException($"エラー応答が返りました\nURL: {errorResponse.Url}", e);
                case Nichan.InvalidResponseException invalidResponse:
                    throw new ChatCollectException($"不正な応答が返りました\nURL: {invalidResponse.Url}", e);
                case Nichan.ResponseException response:
                    throw new ChatCollectException($"応答エラーです\nURL: {response.Url}", e);
                case Nichan.NetworkException network:
                    throw new ChatCollectException($"接続できませんでした\nURL: {network.Url}", e);
                case Nichan.CommunicationException communication:
                    throw new ChatCollectException($"通信エラーです\nURL: {communication.Url}", e);
            }

            ExceptionDispatchInfo.Capture(e).Throw(); // スタックトレースを保って再スロー
        }

        private static readonly HttpClient httpClient = new HttpClient();
        private readonly NichanUtils.INichanThreadSelector threadSelector;
        private readonly TimeSpan threadSelectionUpdateInterval;
        private readonly List<Nichan.Thread> threadList = new List<Nichan.Thread>();
        private readonly Task resCollectLoopTask;
        private readonly CancellationTokenSource resCollectLoopTaskCancellation = new CancellationTokenSource();
        private string[] currentThreadUrls;
        private ChannelInfo lastGetChannel = null;
        private DateTimeOffset lastGetTime;
    }
}
