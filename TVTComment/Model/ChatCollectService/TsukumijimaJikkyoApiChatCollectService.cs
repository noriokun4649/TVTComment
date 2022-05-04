using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model.ChatCollectService
{
    class TsukumijimaJikkyoApiChatCollectService : OnceASecondChatCollectService
    {
        public override string Name => "非公式ニコニコ実況過去ログ";

        public override string GetInformationText()
        {
            if (jkId != 0)
                return $"現在の実況ID: {jkId}";
            else
                return $"現在の実況ID: [対応なし]";
        }

        public override ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public override bool CanPost => false;

        public TsukumijimaJikkyoApiChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry,
            NiconicoUtils.JkIdResolver jkIdResolver
        ) : base(TimeSpan.FromSeconds(10))
        {
            ServiceEntry = serviceEntry;
            this.jkIdResolver = jkIdResolver;
        }

        protected override IEnumerable<Chat> GetOnceASecond(ChannelInfo channel, DateTime time)
        {
            int jkId = jkIdResolver.Resolve(channel.NetworkId, channel.ServiceId);

            if (
                jkId != this.jkId ||
                time >= lastGetStartTime + TimeSpan.FromMinutes(14) ||
                time < lastGetStartTime
            )
            {
                // 前回の取得から14分以上経っている（未来へのシークを含む）か、
                // 前回の取得時刻を超えて過去にシークしたか、実況IDが違うなら再収集
                chatCollectTaskCancellation?.Cancel();
                try
                {
                    chatCollectTask?.Wait();
                }
                catch (AggregateException e)
                {
                    chatCollectTask = null;
                    ChatCollectTaskExceptionHandler(e);
                }
                chatCollectTask = null;

                if (jkId != this.jkId)
                {
                    lock (chats)
                    {
                        chats.Clear();
                    }
                }

                if (jkId != 0)
                {
                    chatCollectTaskCancellation = new CancellationTokenSource();
                    var startTime = new DateTimeOffset(time, TimeSpan.FromHours(9));
                    var endTime = startTime + TimeSpan.FromMinutes(15);
                    chatCollectTask = Task.Run(() => CollectChat(jkId, startTime, endTime, chatCollectTaskCancellation.Token));
                    lastGetStartTime = startTime;
                }
                else
                {
                    chatCollectTaskCancellation = null;
                    chatCollectTask = null;
                }

                this.jkId = jkId;
            }

            try
            {
                chatCollectTask?.Wait();
            }
            catch (AggregateException e)
            {
                chatCollectTask = null;
                ChatCollectTaskExceptionHandler(e);
            }
            chatCollectTask = null;

            lock (chats)
            {
                IEnumerable<Chat> ret = chats.Where(x => time <= x.Chat.Time && x.Chat.Time < time + TimeSpan.FromSeconds(1)).Select(x => x.Chat);
                return ret;
            }
        }

        public override Task PostChat(BasicChatPostObject postObject)
        {
            throw new NotImplementedException();
        }

        public override void Dispose()
        {
            chatCollectTaskCancellation?.Cancel();
            try
            {
                chatCollectTask?.Wait();
            }
            catch (AggregateException e)
            {
                try
                {
                    ChatCollectTaskExceptionHandler(e);
                }
                catch (ChatCollectException)
                { }
            }
            finally
            {
                chatCollectTask = null;
            }
        }

        private async Task CollectChat(int jkId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken)
        {
            string url = $"https://jikkyo.tsukumijima.net/api/kakolog/jk{jkId}?starttime={startTime.ToUnixTimeSeconds()}&endtime={endTime.ToUnixTimeSeconds()}&format=json";

            Stream response;
            try
            {
                response = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == null)
                    throw new ChatCollectException($"接続できませんでした\nURL: {url}", e);
                else
                    throw new ChatCollectException($"HTTP でエラーのステータスコードが返りました\nURL: {url}\nコード: {e.StatusCode}", e);
            }

            JsonDocument jsonDocument;
            try
            {
                jsonDocument = await JsonDocument.ParseAsync(response, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException e)
            {
                throw new ChatCollectException($"不正な応答が返りました\nURL: {url}", e);
            }

            try
            {
                string error = jsonDocument.RootElement.TryGetProperty("error", out JsonElement elem) ? elem.ValueKind == JsonValueKind.String ? elem.GetString() : null : null;
                if (error != null)
                    throw new ChatCollectException($"エラー応答が返されました\nサーバーからのメッセージ: {error}");

                JsonElement packet = jsonDocument.RootElement.GetProperty("packet");

                (string thread, int no)[] threadNoList;
                lock (chats)
                    threadNoList = chats.Select(x => (x.Thread, x.Chat.Number)).ToArray();

                for (int i = 0; i < packet.GetArrayLength(); ++i)
                {
                    JsonElement chatObj = packet[i].GetProperty("chat");
                    string thread = chatObj.GetProperty("thread").GetString();
                    int no = int.Parse(chatObj.GetProperty("no").GetString());

                    if (threadNoList.Contains((thread, no))) // 同じスレッドの同じコメ番があればスキップ
                        continue;

                    if (chatObj.TryGetProperty("deleted", out _)) //削除済みの場合スキップ
                        continue;

                    if (!(chatObj.TryGetProperty("vpos", out elem) && elem.ValueKind == JsonValueKind.String))
                        continue;

                    int vpos = int.Parse(elem.GetString());
                    long date = int.Parse(chatObj.GetProperty("date").GetString());
                    int dateUsec = chatObj.TryGetProperty("date_usec", out elem) ? elem.ValueKind == JsonValueKind.String ? int.Parse(elem.GetString()) : 0 : 0;
                    string mail = chatObj.TryGetProperty("mail", out elem) ? elem.ValueKind == JsonValueKind.String ? elem.GetString() : "" : "";
                    string userId = chatObj.GetProperty("user_id").GetString();
                    int premium = chatObj.TryGetProperty("premium", out elem) ? elem.ValueKind == JsonValueKind.String ? int.Parse(elem.GetString()) : 0 : 0;
                    int anonymity = chatObj.TryGetProperty("anonymity", out elem) ? elem.ValueKind == JsonValueKind.String ? int.Parse(elem.GetString()) : 0 : 0;
                    int abone = chatObj.TryGetProperty("abone", out elem) ? elem.ValueKind == JsonValueKind.String ? int.Parse(elem.GetString()) : 0 : 0;
                    string content = chatObj.GetProperty("content").GetString();

                    var chatTag = new NiconicoUtils.ChatNiconicoCommentXmlTag(
                        content, thread, no, vpos, date, dateUsec, mail, userId, premium, anonymity, abone
                    );

                    lock (chats)
                    {
                        chats.Add((thread, NiconicoUtils.ChatNiconicoCommentXmlTagToChat.Convert(chatTag)));
                    }
                }
            }
            catch (Exception e) when (
                e is InvalidOperationException || e is KeyNotFoundException || e is FormatException
            )
            {
                throw new ChatCollectException("不正な応答が返りました", e);
            }
            finally
            {
                jsonDocument.Dispose();
            }
        }

        private static void ChatCollectTaskExceptionHandler(AggregateException e)
        {
            if (e.InnerExceptions.All(x => x is OperationCanceledException))
                return;
            if (e.InnerExceptions.Count != 1)
                ExceptionDispatchInfo.Capture(e).Throw(); // スタックトレースを保って再スロー

            switch (e.InnerExceptions[0])
            {
                case ChatCollectException chatCollectException:
                    throw chatCollectException;
            }

            ExceptionDispatchInfo.Capture(e).Throw(); // スタックトレースを保って再スロー
        }

        private readonly NiconicoUtils.JkIdResolver jkIdResolver;
        private readonly List<(string Thread, Chat Chat)> chats = new List<(string, Chat)>();
        private int jkId = 0;
        /// <summary>
        /// この時刻から15分後までが前回の取得時間
        /// </summary>
        private DateTimeOffset lastGetStartTime = DateTimeOffset.MinValue;
        private Task chatCollectTask = null;
        private CancellationTokenSource chatCollectTaskCancellation = null;
        private static readonly HttpClient httpClient = new HttpClient();

        static TsukumijimaJikkyoApiChatCollectService()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            string version = assembly.Version.ToString(3);

            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"TvtComment/{version}");
        }
    }
}
