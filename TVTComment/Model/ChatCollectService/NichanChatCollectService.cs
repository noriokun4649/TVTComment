using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TVTComment.Model.ChatCollectService
{
    abstract class NichanChatCollectService : OnceASecondChatCollectService
    {
        public class ChatPostObject : BasicChatPostObject
        {
            public string ThreadUri { get; }

            public ChatPostObject(string threadUri) : base("")
            {
                ThreadUri = threadUri;
            }
        }

        protected abstract string TypeName { get; }
        protected abstract Task<Nichan.Thread> GetThread(string url);

        public override string Name => TypeName + threadSelector switch
        {
            NichanUtils.AutoNichanThreadSelector _ => " ([自動])",
            NichanUtils.FuzzyNichanThreadSelector fuzzy => $" ([類似] {fuzzy.ThreadTitleExample})",
            NichanUtils.KeywordNichanThreadSelector keyword => $" ([キーワード] {string.Join(", ", keyword.Keywords)})",
            NichanUtils.FixedNichanThreadSelector fix => $" ([固定] {string.Join(", ", fix.Uris)})",
            _ => "",
        };
        public override string GetInformationText()
        {
            lock (threads)
            {
                if (threads.Count == 0)
                    return "対応スレがありません";
                else
                    return
                        $"遅延: {chatTimes.Select(x => x.RetrieveTime - x.PostTime).DefaultIfEmpty(TimeSpan.Zero).Max().TotalSeconds}秒\n" +
                        string.Join("\n", threads.Select(pair => $"{pair.Value.Title ?? "[スレタイ不明]"}  ({pair.Value.ResCount})  {pair.Key}"));
            }
        }
        public override ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public override bool CanPost => true;

        public IEnumerable<Nichan.Thread> CurrentThreads
        {
            get
            {
                lock (threads)
                    return threads.Select(x => new Nichan.Thread()
                    {
                        Uri = new Uri(x.Key),
                        Title = x.Value.Title,
                        ResCount = x.Value.ResCount,
                    }).ToArray();
            }
        }

        protected readonly TimeSpan resCollectInterval, threadSearchInterval;
        protected readonly NichanUtils.INichanThreadSelector threadSelector;

        private readonly Task resCollectTask;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();

        /// <summary>
        /// <see cref="ChatCollectException"/>を送出したか
        /// </summary>
        private bool errored = false;

        private ChannelInfo currentChannel;
        private DateTime? currentTime;

        private readonly ConcurrentQueue<Chat> chats = new ConcurrentQueue<Chat>();
        /// <summary>
        /// キーはスレッドのURI
        /// </summary>
        private readonly SortedList<string, (string Title, int ResCount)> threads = new SortedList<string, (string, int)>();
        private readonly List<Chat> chatBuffer = new List<Chat>();
        private readonly List<(DateTime PostTime, DateTime RetrieveTime)> chatTimes = new List<(DateTime, DateTime)>();

        /// <summary>
        /// <see cref="NichanChatCollectService"/>を初期化する
        /// </summary>
        /// <param name="serviceEntry">自分を生んだ<seealso cref="IChatCollectServiceEntry"/></param>
        /// <param name="chatColor">コメント色 <seealso cref="Color.Empty"/>ならランダム色</param>
        /// <param name="resCollectInterval">レスを集める間隔 1秒未満にしても1秒になる</param>
        /// <param name="threadSearchInterval">レスを集めるスレッドのリストを更新する間隔 <paramref name="resCollectInterval"/>未満にしても同じ間隔になる</param>
        /// <param name="threadSelector">レスを集めるスレッドを決める<seealso cref="NichanUtils.INichanThreadSelector"/></param>
        public NichanChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry,
            TimeSpan resCollectInterval, TimeSpan threadSearchInterval,
            NichanUtils.INichanThreadSelector threadSelector
        ) : base(TimeSpan.FromSeconds(10))
        {
            ServiceEntry = serviceEntry;

            if (resCollectInterval < TimeSpan.FromSeconds(1))
                resCollectInterval = TimeSpan.FromSeconds(1);
            if (threadSearchInterval < resCollectInterval)
                threadSearchInterval = resCollectInterval;
            this.resCollectInterval = resCollectInterval;
            this.threadSearchInterval = threadSearchInterval;

            this.threadSelector = threadSelector;

            resCollectTask = Task.Run(() => ResCollectLoop(cancel.Token), cancel.Token);
        }

        private async Task ResCollectLoop(CancellationToken cancellationToken)
        {
            int count = (int)(threadSearchInterval.TotalMilliseconds / resCollectInterval.TotalMilliseconds);
            for (int i = count; !cancellationToken.IsCancellationRequested; i++)
            {
                if (i == count)
                {
                    //スレ一覧を更新する
                    i = 0;
                    if (currentChannel != null && currentTime != null)
                    {
                        IEnumerable<string> threadUris;
                        try
                        {
                            threadUris = threadSelector.Get(
                                currentChannel, new DateTimeOffset(currentTime.Value, TimeSpan.FromHours(9)),
                                cancellationToken
                            ).ToEnumerable();
                        }
                        catch (HttpRequestException e)
                        {
                            throw new ChatCollectException($"収集スレッドリストの更新処理でHTTPエラーが発生しました\n{e}", e);
                        }

                        lock (threads)
                        {
                            //新しいスレ一覧に入ってないものを消す
                            foreach (string uri in threads.Keys.Where(x => !threadUris.Contains(x)).ToList())
                                threads.Remove(uri);
                            //新しいスレ一覧で追加されたものを追加する
                            foreach (string uri in threadUris)
                                if (!threads.ContainsKey(uri))
                                    threads.Add(uri, (null, 0));
                        }
                    }
                    else
                        i = count - 1;//取得対象のチャンネル、時刻が設定されていなければやり直す
                }

                KeyValuePair<string, (string Title, int ResCount)>[] copiedThreads;
                lock (threads)
                    copiedThreads = threads.ToArray();
                foreach (var pair in copiedThreads.ToArray())
                {
                    Nichan.Thread thread = await GetThread(pair.Key);
                    int fromResIdx = thread.Res.FindLastIndex(res => res.Number <= pair.Value.ResCount);
                    fromResIdx++;

                    for (int resIdx = fromResIdx; resIdx < thread.Res.Count; resIdx++)
                    {
                        //1001から先のレスは返さない
                        if (thread.Res[resIdx].Number > 1000)
                            break;
                        foreach (XElement elem in thread.Res[resIdx].Text.Elements("br").ToArray())
                        {
                            elem.ReplaceWith("\n");
                        }
                        chats.Enqueue(new Chat(
                            thread.Res[resIdx].Date.Value, thread.Res[resIdx].Text.Value, Chat.PositionType.Normal, Chat.SizeType.Normal,
                            Color.White, thread.Res[resIdx].UserId, thread.Res[resIdx].Number
                        ));
                    }

                    var pairValue = pair.Value;
                    if (pair.Value.ResCount == 0)
                        pairValue.Title = thread.Title;
                    pairValue.ResCount = thread.Res.Count == 0 ? 0 : thread.Res[^1].Number;
                    lock (threads)
                        threads[pair.Key] = pairValue;
                }
                await Task.Delay(1000, cancellationToken);
            }

        }

        protected override IEnumerable<Chat> GetOnceASecond(ChannelInfo channel, DateTime time)
        {
            currentChannel = channel;
            currentTime = time;

            AggregateException e = resCollectTask.Exception;
            if (e != null)
            {
                errored = true;
                switch (e.InnerExceptions.Count)
                {
                    case 1 when e.InnerExceptions[0] is ChatCollectException:
                        throw e.InnerExceptions[0];
                    case 1 when e.InnerExceptions[0] is HttpRequestException:
                        throw new ChatCollectException($"収集スレッドリストの更新処理でHTTPエラーが発生しました\n{e}", e);
                    case 1 when e.InnerExceptions[0] is InvalidOperationException:
                        throw new ChatCollectException($"収集スレッドリストの更新処理でHTTPエラーが発生しました\n{e}", e);
                    default:
                        resCollectTask.Wait();
                        break;
                }
            }

            var newChats = chats.Where(x => (time - x.Time).Duration() < TimeSpan.FromSeconds(15)).ToArray();//投稿から15秒以内のレスのみ返す
            // 新たに収集したChatをchatBufferに移す
            chats.Clear();
            chatBuffer.AddRange(newChats);
            // 直近15秒のChatの投稿時刻と取得時刻をchatTimesに記憶
            chatTimes.AddRange(newChats.Select(x => (x.Time, time)));
            chatTimes.RemoveAll(x => x.RetrieveTime + TimeSpan.FromSeconds(15) < time);
            // 団子になるのを防ぐため、chatTimes内の時刻差の最大値分だけ遅延してChatを返す
            var delay = chatTimes.Select(x => x.RetrieveTime - x.PostTime).DefaultIfEmpty(TimeSpan.Zero).Max();
            var ret = chatBuffer.Where(x => x.Time + delay <= time).ToArray();
            foreach (var chat in ret)
            {
                chatBuffer.Remove(chat);
            }
            return ret;
        }

#pragma warning disable CS1998 // この非同期メソッドには 'await' 演算子がないため、同期的に実行されます。'await' 演算子を使用して非ブロッキング API 呼び出しを待機するか、'await Task.Run(...)' を使用してバックグラウンドのスレッドに対して CPU 主体の処理を実行することを検討してください。
        public override async Task PostChat(BasicChatPostObject basicChatPostObject)
#pragma warning restore CS1998 // この非同期メソッドには 'await' 演算子がないため、同期的に実行されます。'await' 演算子を使用して非ブロッキング API 呼び出しを待機するか、'await Task.Run(...)' を使用してバックグラウンドのスレッドに対して CPU 主体の処理を実行することを検討してください。
        {
            var chatPostObject = (ChatPostObject)basicChatPostObject;

            string threadUri = chatPostObject.ThreadUri;

            var uri = new UriBuilder(threadUri);
            var pathes = uri.Path.Split('/').ToArray();
            var readcgiIdx = Array.IndexOf(pathes, "read.cgi");
            uri.Path = string.Join('/', pathes[..(readcgiIdx + 3)].Append("1"));
            uri.Fragment = "1";

            Process.Start(new ProcessStartInfo("cmd", $"/c start {uri.ToString().Replace("&", "^&")}"));
        }

        public override void Dispose()
        {
            cancel.Cancel();
            try
            {
                resCollectTask.Wait();
            }
            catch (AggregateException e) when (e.InnerExceptions.All(innerE => innerE is OperationCanceledException))
            { }
            catch (AggregateException)
            {
                if (!errored) throw;
            }
        }
    }

    class HTMLNichanChatCollectService : NichanChatCollectService
    {
        protected override string TypeName => "2chHTML";

        private static readonly HttpClient httpClient = new HttpClient();

        public HTMLNichanChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry,
            TimeSpan resCollectInterval,
            TimeSpan threadSearchInterval,
            NichanUtils.INichanThreadSelector threadSelector
        ) : base(serviceEntry, resCollectInterval, threadSearchInterval, threadSelector)
        {
        }

        protected override async Task<Nichan.Thread> GetThread(string url)
        {
            string response;
            try
            {
                response = await httpClient.GetStringAsync(url).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == null)
                    throw new ChatCollectException($"サーバーとの通信でエラーが発生しました\nURL: {url}", e);
                else
                    throw new ChatCollectException($"サーバーからエラーが返されました\nURL: {url}\nHTTPステータスコード: {e.StatusCode}", e);
            }

            using var textReader = new StringReader(response);
            Nichan.Thread thread;
            try
            {
                thread = Nichan.ThreadParser.ParseFromStream(textReader);
            }
            catch (Nichan.ThreadParserException e)
            {
                throw new ChatCollectException($"対応していないHTMLのドキュメント構造です\nURL: {url}", e);
            }
            thread.Uri = new Uri(url);

            return thread;
        }
    }

    class DATNichanChatCollectService : NichanChatCollectService
    {
        protected override string TypeName => "2chDAT";

        public DATNichanChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry,
            TimeSpan resCollectInterval,
            TimeSpan threadSearchInterval,
            NichanUtils.INichanThreadSelector threadSelector,
            Nichan.ApiClient nichanApiClient
        ) : base(serviceEntry, resCollectInterval, threadSearchInterval, threadSelector)
        {
            apiClient = nichanApiClient;
        }

        protected override async Task<Nichan.Thread> GetThread(string url)
        {
            var uri = new Uri(url);
            var server = uri.Host.Split('.')[0];
            var pathes = uri.Segments.SkipWhile(x => x != "read.cgi/").ToArray();
            var board = pathes[1][..^1];
            var threadID = pathes[2][..^1];

            if (!threadLoaders.TryGetValue((server, board, threadID), out var threadLoader))
            {
                threadLoader = new Nichan.DatThreadLoader(server, board, threadID);
                threadLoaders.Add((server, board, threadID), threadLoader);
            }

            try
            {
                await threadLoader.Update(apiClient);
            }
            catch (Nichan.AuthorizationApiClientException e)
            {
                throw new ChatCollectException("API認証に問題があります。API設定を確認してください", e);
            }
            catch (Nichan.ResponseApiClientException e)
            {
                throw new ChatCollectException("サーバーからの返信にエラーがあります", e);
            }
            catch (Nichan.NetworkApiClientException e)
            {
                throw new ChatCollectException("サーバーに接続できません", e);
            }
            catch (Nichan.DatFormatDatThreadLoaderException e)
            {
                throw new ChatCollectException($"取得したDATのフォーマットが不正です\n\n{e.DatString}", e);
            }

            threadLoader.Thread.Uri = new Uri(url);
            return threadLoader.Thread;
        }

        private readonly Nichan.ApiClient apiClient;
        private readonly Dictionary<(string server, string board, string thread), Nichan.DatThreadLoader> threadLoaders = new Dictionary<(string, string, string), Nichan.DatThreadLoader>();
    }
}
