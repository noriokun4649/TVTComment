using ObservableUtils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.NiconicoUtils;
using TVTComment.Model.NxJikkyoUtils;

namespace TVTComment.Model.ChatCollectService
{
    class NxJikkyoChatCollectService : IChatCollectService
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

        public string Name => "NX-Jikkyo by tsukumi";
        public string GetInformationText()
        {
            int originalJkId = this.originalJkId;
            string ret = $"実況ID: {(originalJkId == 0 ? "[対応する実況IDがありません]" : originalJkId)}";
            return ret;
        }
        public ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public bool CanPost => true;

        private readonly JkIdResolver jk;
        private readonly NxJikkyoCommentReceiver commentReceiver;
        private readonly NxJikkyoCommentSender commentSender;
        private readonly ConcurrentQueue<NiconicoCommentXmlTag> commentTagQueue = new();

        private int originalJkId = 0;
        private Task chatCollectTask = null;
        private Task chatSessionTask = null;
        private CancellationTokenSource cancellationTokenSource = null;

        private BlockingCollection<String> myPostKey = new();

        public NxJikkyoChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry,
            JkIdResolver jkIdResolver
        )
        {
            ServiceEntry = serviceEntry;
            this.jk = jkIdResolver;

            var assembly = Assembly.GetExecutingAssembly().GetName();
            var ua = assembly.Name + "/" + assembly.Version.ToString(3);

            commentReceiver = new NxJikkyoCommentReceiver();
            commentSender = new NxJikkyoCommentSender();
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel, EventInfo _, DateTime time)
        {
            return GetChats(channel);
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel)
        {
            if (chatCollectTask?.IsFaulted ?? false)
            {
                //非同期部分で例外発生
                var e = chatCollectTask.Exception.InnerExceptions.Count == 1
                        ? chatCollectTask.Exception.InnerExceptions[0] : chatCollectTask.Exception;
                // 有志のコミュニティチャンネルで生放送がされてない場合にエラー扱いされると使いづらいので
                if (e is LiveClosedChatReceivingException || e is LiveNotFoundChatReceivingException) {
                }
                else
                {
                    throw new ChatCollectException($"コメント取得でエラーが発生: {e}", chatCollectTask.Exception);
                }
            }

            int jkId = jk.Resolve(channel.NetworkId, channel.ServiceId);

            if (jkId != this.originalJkId)
            {
                // 生放送IDが変更になった場合

                cancellationTokenSource?.Cancel();
                try
                {
                    chatCollectTask?.Wait();
                    chatSessionTask?.Wait();
                }
                //Waitからの例外がタスクがキャンセルされたことによるものか、通信エラー等なら無視
                catch (AggregateException e) when (e.InnerExceptions.All(
                    innerE => innerE is OperationCanceledException || innerE is ChatReceivingException
                ))
                {
                }
                this.originalJkId = jkId;
                commentTagQueue.Clear();

                if (this.originalJkId != 0)
                {
                    cancellationTokenSource = new CancellationTokenSource();
                    chatSessionTask = commentSender.ConnectWatchSession(originalJkId, myPostKey, cancellationTokenSource.Token);
                    chatCollectTask = CollectChat(originalJkId, cancellationTokenSource.Token);
                }
            }

            if (this.originalJkId == 0)
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

        private async Task CollectChat(int jkId, CancellationToken cancellationToken)
        {
            await Task.Delay(2500, cancellationToken).ConfigureAwait(false);
            try
            {
                myPostKey.TryTake(out var postKey, Timeout.Infinite, cancellationToken);

                await foreach (NiconicoCommentXmlTag tag in commentReceiver.Receive(jkId, postKey, cancellationToken))
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
            if (originalJkId != 0)
            {
                try { 
                    await commentSender.Send(originalJkId, chatPostObject.Text, (chatPostObject as ChatPostObject)?.Mail ?? "");
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
            using (myPostKey)
            using (commentReceiver)
            using (commentSender)
            {
                cancellationTokenSource?.Cancel();
                try
                {
                    if (chatCollectTask != null) chatCollectTask.Wait();
                    if (chatSessionTask != null) chatSessionTask.Wait();

                }
                //Waitからの例外がタスクがキャンセルされたことによるものか、通信エラー等なら無視
                catch (AggregateException e) when (e.InnerExceptions.All(
                    innerE => innerE is OperationCanceledException || innerE is ChatReceivingException 
                ))
                {
                }
            }
        }
    }
}
