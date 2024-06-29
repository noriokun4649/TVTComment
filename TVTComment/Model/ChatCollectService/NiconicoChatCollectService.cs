using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;

namespace TVTComment.Model.ChatCollectService
{
    class NiconicoChatCollectService : IChatCollectService
    {
        public class ChatPostObject : BasicChatPostObject
        {
            public string Mail { get; }
            public ChatPostObject(string text, string mail) : base(text)
            {
                Mail = mail;
            }
        }

        public string Name => "ニコニコ実況";
        public ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public bool CanPost { get; }


        private class ChatReceivingException : Exception
        {
            public ChatReceivingException() { }
            public ChatReceivingException(string message) : base(message) { }
            public ChatReceivingException(string message, Exception inner) : base(message, inner) { }
        }

        private readonly NiconicoUtils.JkIdResolver jkIdResolver;
        private int lastJkId = 0;
        private readonly NiconicoUtils.NiconicoCommentXmlParser parser;
        private NetworkStream socketStream;
        private NiconicoUtils.ThreadNiconicoCommentXmlTag lastThreadTag;
        private int lastResNum = -1;
        private string userId;
        private bool isPremium;
        /// <summary>
        /// サーバーからのコメント投稿の結果をセットする
        /// </summary>
        private TaskCompletionSource<int> postingResult;
        private readonly HttpClient httpClient;
        private CancellationTokenSource cancel;
        private Task chatCollectTask;
        private static readonly Encoding utf8Encoding = new UTF8Encoding(false);

        public NiconicoChatCollectService(ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, NiconicoUtils.JkIdResolver jkIdResolver, NiconicoUtils.NiconicoLoginSession session)
        {
            ServiceEntry = serviceEntry;
            this.jkIdResolver = jkIdResolver;

            if (session != null)
            {
                var handler = new HttpClientHandler();
                handler.CookieContainer.Add(session.Cookie);
                httpClient = new HttpClient(handler);
                CanPost = true;
                parser = new NiconicoUtils.NiconicoCommentXmlParser(true,session.UserId);
            }
            else
            {
                httpClient = new HttpClient();
                CanPost = false;
            }
            httpClient.BaseAddress = new Uri("http://jk.nicovideo.jp");
        }

        public string GetInformationText()
        {
            if (lastJkId == 0)
                return "対応する実況IDがありません";
            string ret = $"実況ID: {lastJkId} - ";
            if (socketStream == null)
                return ret + "サーバーに未接続";
            else
                return ret + "サーバーに接続中";
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel, EventInfo _, DateTime time)
        {
            return GetChats(channel, time);
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel, DateTime time)
        {
            if (chatCollectTask != null && chatCollectTask.IsFaulted)
            {
                //非同期部分で例外発生
                throw new ChatCollectException($"ニコニコ実況でエラーが発生しました: {chatCollectTask.Exception}", chatCollectTask.Exception);
            }

            int jkId = jkIdResolver.Resolve(channel.NetworkId, channel.ServiceId);
            if (jkId == 0)
            {
                lastJkId = 0;
                return Array.Empty<Chat>();
            }

            //非同期部分で集めたデータからチャットを生成
            var ret = new List<Chat>();
            lock (parser)
            {
                while (parser.DataAvailable())
                {
                    var tag = parser.Pop();
                    if (tag is NiconicoUtils.ChatNiconicoCommentXmlTag chatTag)
                    {
                        var chat = NiconicoUtils.ChatNiconicoCommentXmlTagToChat.Convert(chatTag);
                        ret.Add(chat);
                        lastResNum = chat.Number;
                    }
                    else if (tag as NiconicoUtils.ThreadNiconicoCommentXmlTag != null)
                        lastThreadTag = tag as NiconicoUtils.ThreadNiconicoCommentXmlTag;
                    else if (tag is NiconicoUtils.ChatResultNiconicoCommentXmlTag chatResultTag)
                        postingResult?.TrySetResult(chatResultTag.Status);

                }
            }

            if (lastJkId == jkId)
                return ret;

            //選択番組に変更があった場合
            lastJkId = jkId;
            lock (parser)
                parser.Reset();
            try
            {
                cancel?.Cancel();
                try
                {
                    chatCollectTask?.Wait();
                }
                //Waitからの例外が、タスクがキャンセルされたことによるものなら無視
                catch (AggregateException e) when (e.InnerExceptions.All(innerE => innerE is OperationCanceledException))
                {
                }
                cancel = new CancellationTokenSource();
                chatCollectTask = CollectChat(jkId, cancel.Token);
            }
            catch (ChatReceivingException e)
            {
                throw new ChatCollectException($"ニコニコ実況でエラーが発生しました: {e}", e);
            }
            return ret;
        }

        private async Task CollectChat(int jkId, CancellationToken cancel)
        {
            try
            {
                cancel.Register(() =>
                {
                    httpClient.CancelPendingRequests();
                    socketStream?.Dispose();//ReadAsyncはキャンセルしてもすぐに戻らないので無理やり切る
                    socketStream = null;
                });

                for (int disconnectedCount = 0; ; disconnectedCount++)
                {
                    string str = await httpClient.GetStringAsync($"http://jk.nicovideo.jp/api/getflv?v=jk{jkId}", cancel).ConfigureAwait(false);
                    var query = HttpUtility.ParseQueryString(str);

                    if (query["error"] != null)
                        throw new ChatReceivingException("コメントサーバーからエラーが返されました");

                    string thread_id = query["thread_id"];
                    string ms = query["ms"];
                    string ms_port = query["ms_port"];
                    userId = query["user_id"];
                    isPremium = int.Parse(query["is_premium"] ?? "0") != 0;

                    string body = $"<thread res_from=\"-10\" version=\"20061206\" thread=\"{thread_id}\" />\0";
                    using TcpClient tcpClinet = new TcpClient(ms, int.Parse(ms_port));
                    try
                    {
                        socketStream = tcpClinet.GetStream();

                        byte[] body_encoded = utf8Encoding.GetBytes(body);
                        await socketStream.WriteAsync(body_encoded.AsMemory(0, body_encoded.Length), cancel).ConfigureAwait(false);

                        //コメント受信ループ
                        while (true)
                        {
                            byte[] buf = new byte[2048];
                            int receivedByte = await socketStream.ReadAsync(buf.AsMemory(0, buf.Length), cancel).ConfigureAwait(false);
                            if (receivedByte == 0)
                                throw new ChatReceivingException("コメントサーバーとの通信が切断されました");

                            //TODO: データが文字境界以外で切れることを想定してない
                            lock (parser)
                                parser.Push(utf8Encoding.GetString(buf, 0, receivedByte));
                        }
                    }
                    catch (ChatReceivingException)
                    {
                        if (disconnectedCount >= 3)
                            throw;
                        else
                            continue;
                    }
                    finally
                    {
                        socketStream?.Dispose();
                        socketStream = null;
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                if (cancel.IsCancellationRequested)
                    throw new OperationCanceledException("Receive loop was canceled", e, cancel);
                throw;
            }
            catch (Exception e) when (e is HttpRequestException || e is SocketException || e is IOException)
            {
                if (cancel.IsCancellationRequested)
                    throw new OperationCanceledException("Receive loop was canceled", e, cancel);
                throw new ChatReceivingException("コメントサーバーとの通信でエラーが発生しました", e);
            }
        }

        public async Task PostChat(BasicChatPostObject chatPostObject)
        {
            if (!CanPost)
                throw new NotSupportedException("Posting is not supprted on this ChatCollectService");

            if (lastThreadTag == null || lastResNum == -1 || socketStream == null || postingResult != null)
                throw new ChatPostException("コメントが投稿できる状態にありません。しばらく待ってから再試行してください。");

            //postkey取得
            string postKey = await httpClient.GetStringAsync($"/api/v2/getpostkey?thread={lastThreadTag.Thread}&block_no={(lastResNum + 1) / 100}");
            postKey = postKey[(postKey.IndexOf('=') + 1)..];

            // vposは10msec単位 サーバ時刻を基準に計算
            int vpos = (int)(lastThreadTag.ServerTime - lastThreadTag.Thread) * 100 + (int)((GetDateTimeJstNow() - lastThreadTag.ReceivedTime).Value.TotalMilliseconds) / 10;

            string mail = (chatPostObject as ChatPostObject)?.Mail ?? "";

            XElement postXml = new XElement("chat",
                new XAttribute("thread", lastThreadTag.Thread),
                new XAttribute("ticket", lastThreadTag.Ticket),
                new XAttribute("vpos", vpos),
                new XAttribute("postkey", postKey),
                new XAttribute("mail", mail),
                new XAttribute("user_id", userId),
                new XAttribute("premium", isPremium ? "1" : "0"),
                new XAttribute("staff", "0"),
                new XText(chatPostObject.Text));
            byte[] postData = utf8Encoding.GetBytes(postXml.ToString(SaveOptions.DisableFormatting) + "\0");//最後にnull文字が必要

            postingResult = new TaskCompletionSource<int>();//コメント受信側で投稿の結果応答を入れてもらう
            await socketStream.WriteAsync(postData.AsMemory(0, postData.Length));

            int result;
            using (new Timer((_) => postingResult?.TrySetCanceled(), null, 5000, Timeout.Infinite))//結果応答を5秒以内に受信できなければタイムアウト
            {
                try
                {
                    await postingResult.Task;//結果が来るまで待つ
                    result = postingResult.Task.Result;
                }
                catch (OperationCanceledException)
                {
                    //タイムアウトした
                    throw new ChatPostException("コメントを送信しましたがサーバーから結果応答を受信できませんでした。");
                }
                finally
                {
                    postingResult = null;
                }
            }

            if (result != 0)
                throw new ChatPostException($"コメント投稿でサーバーからエラーが返されました。エラーコードは'{result}'です。");
        }

        public void Dispose()
        {
            using (httpClient)
            {
                cancel?.Cancel();
                cancel = null;
                try
                {
                    chatCollectTask?.Wait();
                }
                //Waitからの例外がタスクがキャンセルされたことによるものか、通信エラーなら無視
                catch (AggregateException e) when (e.InnerExceptions.All(innerE => innerE is OperationCanceledException || innerE is ChatReceivingException))
                {
                }
                chatCollectTask = null;
            }
        }

        /// <summary>
        /// (マシンのカルチャ設定に関係なく)今の日本標準時を返す
        /// </summary>
        private static DateTime GetDateTimeJstNow()
        {
            return DateTime.SpecifyKind(DateTime.UtcNow.AddHours(9), DateTimeKind.Unspecified);
        }
    }
}
