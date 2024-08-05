using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.NiconicoUtils;

namespace TVTComment.Model.ChatCollectService
{
    class NewNiconicoJikkyouChatCollectService : IChatCollectService
    {
        public class ChatPostObject : BasicChatPostObject
        {
            public string Mail { get; }
            public ChatPostObject(string text, string mail) : base(text)
            {
                Mail = mail;
            }
        }

        private class ChatReceivingException : Exception
        {
            public ChatReceivingException(string message) : base(message) { }
            public ChatReceivingException(string message, Exception inner) : base(message, inner) { }
        }
        private class LiveClosedChatReceivingException : ChatReceivingException
        {
            public LiveClosedChatReceivingException() : base("放送終了後です")
            { }
        }
        private class LiveNotFoundChatReceivingException : ChatReceivingException
        {
            public LiveNotFoundChatReceivingException() : base("生放送が見つかりません")
            { }
        }

        public string Name => "新ニコニコ実況(帰ってきた)";
        public string GetInformationText()
        {
            string originalLiveId = this.originalLiveId;
            string ret = $"生放送ID: {(originalLiveId == "" ? "[対応する生放送IDがありません]" : originalLiveId)}";
            if (originalLiveId != "")
                ret += $"\n状態: {(notOnAir ? "放送していません" : "放送中")}";
            return ret;
        }
        public ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public bool CanPost => true;

        private readonly LiveIdResolver liveIdResolver;
        private readonly NewNicoLiveCommentReciver commentReceiver;
        private readonly NicoLiveCommentSender commentSender;
        private readonly ConcurrentQueue<NiconicoCommentXmlTag> commentTagQueue = new ConcurrentQueue<NiconicoCommentXmlTag>();

        private string originalLiveId = "";
        private bool notOnAir = false;
        private Task chatCollectTask = null;
        private Task chatSessionTask = null;
        private CancellationTokenSource cancellationTokenSource = null;

        private BlockingCollection<MessageServer> messageServers = [];

        public NewNiconicoJikkyouChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry,
            LiveIdResolver liveIdResolver,
            NiconicoLoginSession niconicoLoginSession
        )
        {
            ServiceEntry = serviceEntry;
            this.liveIdResolver = liveIdResolver;

            commentReceiver = new NewNicoLiveCommentReciver(niconicoLoginSession);
            commentSender = new NicoLiveCommentSender(niconicoLoginSession);
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
                // 有志のコミュニティチャンネルで生放送がされてない場合にエラー扱いされると使いづらいので
                if (e is LiveClosedChatReceivingException || e is LiveNotFoundChatReceivingException)
                    notOnAir = true;
                else
                    throw new ChatCollectException($"コメント取得でエラーが発生: {e}", chatCollectTask.Exception);
            }

            var liveId = liveIdResolver.Resolve(channel.NetworkId, channel.ServiceId).ToString();

            if (liveId != this.originalLiveId)
            {
                // 生放送IDが変更になった場合

                cancellationTokenSource?.Cancel();
                try
                {
                    chatSessionTask?.Wait();
                }
                //Waitからの例外がタスクがキャンセルされたことによるものか、通信エラー等なら無視
                catch (AggregateException e) when (e.InnerExceptions.All(
                    innerE => innerE is OperationCanceledException || innerE is ChatReceivingException
                ))
                {
                }
                this.originalLiveId = liveId;
                commentTagQueue.Clear();
                notOnAir = false;

                if (this.originalLiveId != "")
                {
                    cancellationTokenSource = new CancellationTokenSource();
                    chatSessionTask = commentSender.ConnectWatchSession(originalLiveId, messageServers, cancellationTokenSource.Token);
                    chatCollectTask = CollectChat(cancellationTokenSource.Token);
                }
            }

            if (this.originalLiveId == "")
            {
                return Array.Empty<Chat>();
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
            if (originalLiveId != "")
            {
                try
                {
                    await commentSender.Send(originalLiveId, chatPostObject.Text, (chatPostObject as ChatPostObject)?.Mail ?? "");
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
                cancellationTokenSource?.Cancel();
                try
                {
                    chatSessionTask?.Wait();
                }
                //Waitからの例外がタスクがキャンセルされたことによるものか、通信エラー等なら無視
                catch (AggregateException e) when (e.InnerExceptions.All(
                    innerE => innerE is OperationCanceledException || innerE is ChatReceivingException || innerE is NetworkNicoLiveCommentReceiverException
                ))
                {
                }
            }
        }
    }
}
