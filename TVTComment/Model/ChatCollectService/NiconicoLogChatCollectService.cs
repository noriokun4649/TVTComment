using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace TVTComment.Model.ChatCollectService
{
    class NiconicoLogChatCollectService : OnceASecondChatCollectService
    {
        private class ServerErrorException : Exception
        {
            public ServerErrorException() { }
            public ServerErrorException(string message) : base(message) { }
            public ServerErrorException(string message, Exception inner) : base(message, inner) { }
        }

        private readonly NiconicoUtils.JkIdResolver jkIdResolver;
        private int lastJkId = 0;
        private DateTime lastGetTime;
        private long baseTime;
        private readonly List<NiconicoUtils.ChatAndVpos> chats = new List<NiconicoUtils.ChatAndVpos>();
        private Task chatCollectTask;
        private readonly HttpClient client;
        private readonly NiconicoUtils.NiconicoCommentXmlParser parser;
        public override string Name => "ニコニコ実況過去ログ";
        public override ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }

        public override string GetInformationText()
        {
            if (lastJkId != 0)
                return $"現在の実況ID: {lastJkId}, 前回の取得時刻: {lastGetTime:HH:mm:ss}";
            else
                return $"現在の実況ID: [対応なし]";
        }

        public NiconicoLogChatCollectService(ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, NiconicoUtils.JkIdResolver jkIdResolver, NiconicoUtils.NiconicoLoginSession session) : base(new TimeSpan(0, 0, 10))
        {
            parser = new NiconicoUtils.NiconicoCommentXmlParser(false, session.UserId);
            ServiceEntry = serviceEntry;
            this.jkIdResolver = jkIdResolver;
            var handler = new HttpClientHandler();
            client = new HttpClient(handler);
            handler.CookieContainer.Add(session.Cookie);
        }

        protected override IEnumerable<Chat> GetOnceASecond(ChannelInfo channel, DateTime time)
        {
            List<Chat> ret = new List<Chat>();

            int jkId = jkIdResolver.Resolve(channel.NetworkId, channel.ServiceId);

            if (jkId == 0)
            {
                //対応するjkidがなかった
                lastJkId = 0;
                return ret;
            }

            if (lastJkId == jkId && baseTime != 0)
            {
                lock (chats)
                {
                    //再生時刻のchatをretに入れる
                    int vpos = (int)(new DateTimeOffset(time).ToUnixTimeSeconds() - baseTime) * 100;//time.KindはLocalじゃないとダメよ

                    foreach (var chat in chats.Where(chat => vpos <= chat.Vpos && chat.Vpos < vpos + 100).Select(chat => chat.Chat))
                        ret.Add(chat);
                    chats.RemoveAll(chat => chat.Vpos < vpos + 100);
                }
            }

            //10秒に一回かチャンネルが変わったら取得
            if ((time - lastGetTime).Duration().TotalSeconds < 10 && lastJkId == jkId)
                return ret;//取得しないなら帰る

            //取得処理
            if (chatCollectTask != null && !chatCollectTask.IsCompleted)
                return ret;//まだ前回の取得が終わってないなら帰る(普通は終わってるはず)

            try
            {
                chatCollectTask?.Wait();
            }
            catch (AggregateException e) when (e.InnerException is ServerErrorException)
            {
                throw new ChatCollectException("コメントサーバーからエラーが返されました。", e);
            }
            catch (AggregateException e) when (e.InnerException is HttpRequestException)
            {
                throw new ChatCollectException("コメントサーバーとの通信でエラーが発生しました。このエラーはサーバー混雑時にも起こり得ます。", e);
            }

            //シークとかしてなければ前回取得分以降のコメ取得、そうでなければ250個分取得
            int resFrom;
            lock (chats)
            {
                if (chats.Count > 0 && (time - lastGetTime).Duration().TotalSeconds < 15 && lastJkId == jkId)
                {
                    resFrom = chats[^1].Chat.Number + 1;
                }
                else
                {
                    resFrom = -250;
                    chats.Clear();
                }
            }

            chatCollectTask = CollectChat(jkId, time.AddSeconds(1), time.AddSeconds(15), resFrom);

            lastJkId = jkId;
            lastGetTime = time;

            return ret;
        }

        private async Task CollectChat(int jkId, DateTime startTime, DateTime endTime, int resFrom)
        {
            string startTimeStr = new DateTimeOffset(startTime).ToUnixTimeSeconds().ToString();
            string endTimeStr = new DateTimeOffset(endTime).ToUnixTimeSeconds().ToString();

            string queryStr = await client.GetStringAsync($"http://jk.nicovideo.jp/api/getflv?v=jk{jkId}&start_time={startTimeStr}&end_time={endTimeStr}").ConfigureAwait(false);
            var query = HttpUtility.ParseQueryString(queryStr);
            if (query.AllKeys.Contains("error"))
                throw new ServerErrorException();

            string thread_id = query["thread_id"];
            string ms = query["ms"];
            string http_port = query["http_port"];
            string user_id = query["user_id"];
            baseTime = long.Parse(query["base_time"]);

            string waybackkey = await client.GetStringAsync($"http://jk.nicovideo.jp/api/v2/getwaybackkey?thread={thread_id}").ConfigureAwait(false);
            waybackkey = waybackkey[(waybackkey.IndexOf('=') + 1)..];

            string data = await client.GetStringAsync($"http://{ms}:{http_port}/api/thread?thread={thread_id}&res_from={resFrom}&version=20061206&when={endTimeStr}&user_id={user_id}&waybackkey={waybackkey}&scores=1").ConfigureAwait(false);

            
            parser.Push(data);
            lock (chats)
            {
                while (parser.DataAvailable())
                {
                    var tag = parser.Pop();
                    if (tag as NiconicoUtils.ChatNiconicoCommentXmlTag != null)
                        chats.Add(new NiconicoUtils.ChatAndVpos(
                            NiconicoUtils.ChatNiconicoCommentXmlTagToChat.Convert(tag as NiconicoUtils.ChatNiconicoCommentXmlTag), (tag as NiconicoUtils.ChatNiconicoCommentXmlTag).Vpos
                        ));
                }
            }
        }

        public override void Dispose()
        {
            using (client)
            {
                client.CancelPendingRequests();
                try
                {
                    chatCollectTask?.Wait();
                }
                catch (AggregateException e) when (e.InnerException is OperationCanceledException || e.InnerException is HttpRequestException || e.InnerException is ServerErrorException) { }
            }
        }
    }
}
