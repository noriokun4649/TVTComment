using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.NiconicoUtils;

namespace TVTComment.Model.ChatCollectService
{
    class NiconicoLiveChatCollectService : IChatCollectService
    {
        public class ChatPostObject : BasicChatPostObject
        {
            public string Mail { get; }
            public ChatPostObject(string text, string mail) : base(text)
            {
                Mail = mail;
            }
        }

        public string Name => "ニコニコ生放送";
        public ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public bool CanPost => true;


        private class ChatReceivingException : Exception
        {
            public ChatReceivingException() { }
            public ChatReceivingException(string message) : base(message) { }
            public ChatReceivingException(string message, Exception inner) : base(message, inner) { }
        }

        private class LiveClosedChatReceivingException : ChatReceivingException
        {
            public LiveClosedChatReceivingException() : base("放送終了後です")
            { }
        }

        private readonly string liveId;
        private BlockingCollection<MessageServer> messageServers = [];

        private readonly HttpClient httpClient;
        private readonly Task chatCollectTask;
        private readonly Task chatSessionTask;
        private readonly ConcurrentQueue<NiconicoUtils.NiconicoCommentXmlTag> commentTagQueue = new();
        private readonly NewNicoLiveCommentReciver commentReceiver;
        private readonly NiconicoUtils.NicoLiveCommentSender commentSender;
        private DateTime lastHeartbeatTime = DateTime.MinValue;
        private readonly CancellationTokenSource cancel = new();

        public NiconicoLiveChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, string liveId,
            NiconicoUtils.NiconicoLoginSession session
        )
        {
            ServiceEntry = serviceEntry;
            this.liveId = liveId;

            var assembly = Assembly.GetExecutingAssembly().GetName();
            var ua = assembly.Name + "/" + assembly.Version.ToString(3);

            var handler = new HttpClientHandler();
            handler.CookieContainer.Add(session.Cookie);
            httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua);

            commentReceiver = new NewNicoLiveCommentReciver(session);
            commentSender = new NiconicoUtils.NicoLiveCommentSender(session);

            chatCollectTask = CollectChat(cancel.Token);
            chatSessionTask = commentSender.ConnectWatchSession(this.liveId, messageServers, cancel.Token);
        }

        public string GetInformationText()
        {
            return $"生放送ID: {liveId}";
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel, EventInfo _, DateTime time)
        {
            return GetChats(channel, time);
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel, DateTime time)
        {
            if (chatCollectTask?.IsFaulted ?? false)
            {
                //非同期部分で例外発生
                var e = chatCollectTask.Exception.InnerExceptions.Count == 1
                        ? chatCollectTask.Exception.InnerExceptions[0] : chatCollectTask.Exception;
                throw new ChatCollectException($"コメント取得でエラーが発生: {e}", chatCollectTask.Exception);
            }
            if (chatSessionTask?.IsFaulted ?? false)
            {
                //非同期部分で例外発生
                var e = chatSessionTask.Exception.InnerExceptions.Count == 1
                        ? chatSessionTask.Exception.InnerExceptions[0] : chatSessionTask.Exception;
                    throw new ChatCollectException($"コメント取得でエラーが発生: {e}", chatSessionTask.Exception);
            }

            var ret = new List<Chat>();
            while (commentTagQueue.TryDequeue(out var tag))
            {
                switch (tag)
                {
                    case ChatNiconicoCommentXmlTag chatTag:
                        ret.Add(ChatNiconicoCommentXmlTagToChat.Convert(chatTag));
                        break;
                }
            }
            return ret;
        }

        private async Task CollectChat(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(2500, cancellationToken).ConfigureAwait(false);
                //コメント投稿(視聴)セッションのRoomメッセージでPostKeyを取得出来るまでロックして待機
                messageServers.TryTake(out var message, Timeout.Infinite);

                await foreach (NiconicoCommentXmlTag tag in commentReceiver.Receive(message, cancellationToken))
                {
                    commentTagQueue.Enqueue(tag);
                }
            }
            catch (InvalidPlayerStatusNicoLiveCommentReceiverException e)
            {
                throw new ChatReceivingException("サーバーから予期しないPlayerStatusが返されました:\n" + e.PlayerStatus, e);
            }
            catch (NetworkNicoLiveCommentReceiverException e)
            {
                throw new ChatReceivingException("サーバーとの通信でエラーが発生しました", e);
            }
            catch (ConnectionClosedNicoLiveCommentReceiverException e)
            {
                throw new ChatReceivingException("サーバーとの通信が切断されました", e);
            }
            catch (ConnectionDisconnectNicoLiveCommentReceiverException)
            {
                throw new LiveClosedChatReceivingException();
            }
        }

        public async Task PostChat(BasicChatPostObject chatPostObject)
        {
            if (liveId != "")
            {
                try
                {
                    await commentSender.Send(liveId, chatPostObject.Text, (chatPostObject as ChatPostObject)?.Mail ?? "");
                }
                catch (ResponseFormatNicoLiveCommentSenderException e)
                {
                    throw new ChatPostException($"サーバーからエラーが返されました\n{e.Response}", e);
                }
                catch (NicoLiveCommentSenderException e)
                {
                    throw new ChatPostException($"サーバーに接続できませんでした\n{e.Message}", e);
                }
            }
            else
            {
                throw new ChatPostException("コメントが投稿できる状態にありません。しばらく待ってから再試行してください。");
            }
        }

        public void Dispose()
        {
            using (messageServers)
            using (commentReceiver)
            using (commentSender)
            {
                cancel?.Cancel();
                try
                {
                    chatSessionTask?.Wait();
                }
                //Waitからの例外がタスクがキャンセルされたことによるものか、通信エラー等なら無視
                catch (AggregateException e) when (e.InnerExceptions.All(
                    innerE => innerE is OperationCanceledException || innerE is ChatReceivingException ||
                    innerE is NetworkNicoLiveCommentReceiverException || innerE is NicoLiveCommentSenderException
                ))
                {
                }
            }
        }
    }
}
