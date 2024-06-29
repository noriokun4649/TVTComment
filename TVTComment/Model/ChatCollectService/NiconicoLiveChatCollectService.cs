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

        private string originalLiveId;
        private string liveId = "";
        private BlockingCollection<String> myPostKey = new();

        private readonly HttpClient httpClient;
        private readonly Task chatCollectTask;
        private readonly Task chatSessionTask;
        private readonly ConcurrentQueue<NiconicoUtils.NiconicoCommentXmlTag> commentTagQueue = new ConcurrentQueue<NiconicoUtils.NiconicoCommentXmlTag>();
        private readonly NiconicoUtils.NicoLiveCommentReceiver commentReceiver;
        private readonly NiconicoUtils.NicoLiveCommentSender commentSender;
        private DateTime lastHeartbeatTime = DateTime.MinValue;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();

        public NiconicoLiveChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, string liveId,
            NiconicoUtils.NiconicoLoginSession session
        )
        {
            ServiceEntry = serviceEntry;
            originalLiveId = liveId;

            var assembly = Assembly.GetExecutingAssembly().GetName();
            var ua = assembly.Name + "/" + assembly.Version.ToString(3);

            var handler = new HttpClientHandler();
            handler.CookieContainer.Add(session.Cookie);
            httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua);

            commentReceiver = new NiconicoUtils.NicoLiveCommentReceiver(session);
            commentSender = new NiconicoUtils.NicoLiveCommentSender(session);

            chatCollectTask = CollectChat(cancel.Token);
            chatSessionTask = commentSender.ConnectWatchSession(originalLiveId, cancel.Token, myPostKey);
        }

        public string GetInformationText()
        {
            return $"生放送ID: {originalLiveId}" + (liveId != "" ? $" ({liveId})" : "");
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel, EventInfo _, DateTime time)
        {
            return GetChats(channel, time);
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel, DateTime time)
        {
            if (chatCollectTask.IsCanceled)
            {
                throw new ChatCollectException("放送が終了しました");
            }
            if (chatCollectTask.IsFaulted)
            {
                //非同期部分で例外発生
                var e = chatCollectTask.Exception.InnerExceptions.Count == 1 ? chatCollectTask.Exception.InnerExceptions[0] : chatCollectTask.Exception;
                throw new ChatCollectException(
                    e is ChatReceivingException && e.InnerException == null ? e.Message : $"コメント取得でエラーが発生: {e}",
                    chatCollectTask.Exception
                );
            }

            if (chatSessionTask?.IsFaulted ?? false)
            {
                //非同期部分で例外発生
                var e = chatSessionTask.Exception.InnerExceptions.Count == 1
                        ? chatSessionTask.Exception.InnerExceptions[0] : chatCollectTask.Exception;
                throw new ChatPostException($"視聴セッションでエラーが発生: {e}", chatSessionTask.Exception);
            }

            //非同期部分で集めたデータからチャットを生成
            var ret = new List<Chat>();
            while (commentTagQueue.TryDequeue(out var tag))
            {
                if (tag as NiconicoUtils.ChatNiconicoCommentXmlTag != null)
                    ret.Add(NiconicoUtils.ChatNiconicoCommentXmlTagToChat.Convert(tag as NiconicoUtils.ChatNiconicoCommentXmlTag));
                else if (tag is NiconicoUtils.LeaveThreadNiconicoCommentXmlTag)
                    cancel.Cancel();
            }
            return ret;
        }

        private async Task CollectChat(CancellationToken cancel)
        {
            Stream playerStatusStr;
            try
            {
                if (!originalLiveId.StartsWith("lv")) // 代替えAPIではコミュニティ・チャンネルにおけるコメント鯖取得ができないのでlvを取得しに行く
                {
                    var getLiveId = await httpClient.GetStreamAsync($"https://live2.nicovideo.jp/unama/tool/v1/broadcasters/social_group/{originalLiveId}/program", cancel).ConfigureAwait(false);
                    var liveIdJson = await JsonDocument.ParseAsync(getLiveId, cancellationToken: cancel).ConfigureAwait(false);
                    var liveIdRoot = liveIdJson.RootElement;
                    if (!liveIdRoot.GetProperty("meta").GetProperty("errorCode").GetString().Equals("OK")) throw new ChatReceivingException("コミュニティ・チャンネルが見つかりませんでした");
                    originalLiveId = liveIdRoot.GetProperty("data").GetProperty("nicoliveProgramId").GetString(); // lvから始まるLiveIDに置き換え

                }
                playerStatusStr = await httpClient.GetStreamAsync($"https://live2.nicovideo.jp/unama/watch/{originalLiveId}/programinfo", cancel).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new ChatReceivingException("番組が見つかりません\n権限がないか削除された可能性があります");
                if (e.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    throw new ChatReceivingException("ニコニコのサーバーがメンテナンス中の可能性があります");
                if (e.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    throw new ChatReceivingException("ニコニコのサーバーで内部エラーが発生しました");

                throw new ChatReceivingException("サーバーとの通信でエラーが発生しました", e);
            }

            var playerStatus = await JsonDocument.ParseAsync(playerStatusStr, cancellationToken: cancel).ConfigureAwait(false);
            var playerStatusRoot = playerStatus.RootElement;

            if (playerStatusRoot.GetProperty("data").GetProperty("rooms").GetArrayLength() <= 0)
                throw new ChatReceivingException("コメント取得できませんでした以下の原因が考えられます\n\n・放送されていない\n・視聴権がない\n・コミュニティフォロワー限定番組");
            
            liveId = playerStatusRoot.GetProperty("data").GetProperty("socialGroup").GetProperty("id").GetString();

            try
            {
                //コメント投稿(視聴)セッションのRoomメッセージでPostKeyを取得出来るまでロックして待機
                myPostKey.TryTake(out var postKey, Timeout.Infinite);
                
                await foreach (NiconicoUtils.NiconicoCommentXmlTag tag in commentReceiver.Receive(originalLiveId, cancel, postKey))
                {
                    commentTagQueue.Enqueue(tag);
                }
            }
            catch (NiconicoUtils.InvalidPlayerStatusNicoLiveCommentReceiverException e)
            {
                throw new ChatReceivingException("サーバーから予期しないPlayerStatusが返されました:\n" + e.PlayerStatus, e);
            }
            catch (NiconicoUtils.NetworkNicoLiveCommentReceiverException e)
            {
                throw new ChatReceivingException("サーバーとの通信でエラーが発生しました" + liveId, e);
            }
            catch (NiconicoUtils.ConnectionClosedNicoLiveCommentReceiverException e)
            {
                throw new ChatReceivingException("サーバーとの通信が切断されました", e);
            }
            catch (NiconicoUtils.ConnectionDisconnectNicoLiveCommentReceiverException)
            {
                throw new ChatReceivingException("放送が終了しました");
            }
        }

        public async Task PostChat(BasicChatPostObject chatPostObject)
        {
            if (liveId == "")
                throw new ChatPostException("コメントが投稿できる状態にありません。しばらく待ってから再試行してください。");
            await commentSender.Send(liveId, chatPostObject.Text, (chatPostObject as ChatPostObject)?.Mail ?? "");
        }

        public void Dispose()
        {
            using (commentReceiver)
            using (commentSender)
            using (httpClient)
            {
                cancel.Cancel();
                try
                {
                    chatCollectTask.Wait();
                }
                //Waitからの例外がタスクがキャンセルされたことによるものか、通信エラーなら無視
                catch (AggregateException e) when (e.InnerExceptions.All(innerE => innerE is OperationCanceledException || innerE is ChatReceivingException))
                {
                }
            }
        }
    }
}
