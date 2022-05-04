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

        public string Name => "新ニコニコ実況";
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

        private readonly NiconicoUtils.LiveIdResolver liveIdResolver;
        private readonly HttpClient httpClient;
        private readonly NiconicoUtils.NicoLiveCommentReceiver commentReceiver;
        private readonly NiconicoUtils.NicoLiveCommentSender commentSender;
        private readonly ConcurrentQueue<NiconicoUtils.NiconicoCommentXmlTag> commentTagQueue = new ConcurrentQueue<NiconicoUtils.NiconicoCommentXmlTag>();

        private string originalLiveId = "";
        private string liveId = "";
        private bool notOnAir = false;
        private Task chatCollectTask = null;
        private Task chatSessionTask = null;
        private CancellationTokenSource cancellationTokenSource = null;

        public NewNiconicoJikkyouChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry,
            NiconicoUtils.LiveIdResolver liveIdResolver,
            NiconicoUtils.NiconicoLoginSession niconicoLoginSession
        )
        {
            ServiceEntry = serviceEntry;
            this.liveIdResolver = liveIdResolver;

            var assembly = Assembly.GetExecutingAssembly().GetName();
            var ua = assembly.Name + "/" + assembly.Version.ToString(3);

            var handler = new HttpClientHandler();
            handler.CookieContainer.Add(niconicoLoginSession.Cookie);
            httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua);

            commentReceiver = new NiconicoUtils.NicoLiveCommentReceiver(niconicoLoginSession);
            commentSender = new NiconicoUtils.NicoLiveCommentSender(niconicoLoginSession);
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

            string originalLiveId = liveIdResolver.Resolve(channel.NetworkId, channel.ServiceId);

            if (originalLiveId != this.originalLiveId)
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
                this.originalLiveId = originalLiveId;
                commentTagQueue.Clear();
                notOnAir = false;

                if (this.originalLiveId != "")
                {
                    cancellationTokenSource = new CancellationTokenSource();
                    chatCollectTask = CollectChat(originalLiveId, cancellationTokenSource.Token);
                    chatSessionTask = commentSender.ConnectWatchSession(originalLiveId, cancellationTokenSource.Token);
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
                    case NiconicoUtils.ChatNiconicoCommentXmlTag chatTag:
                        ret.Add(NiconicoUtils.ChatNiconicoCommentXmlTagToChat.Convert(chatTag));
                        break;
                }
            }
            return ret;
        }

        private async Task CollectChat(string originalLiveId, CancellationToken cancellationToken)
        {
            Stream playerStatusStr;
            try
            {
                if (!originalLiveId.StartsWith("lv")) // 代替えAPIではコミュニティ・チャンネルにおけるコメント鯖取得ができないのでlvを取得しに行く
                {
                    var getLiveId = await httpClient.GetStreamAsync($"https://live2.nicovideo.jp/unama/tool/v1/broadcasters/social_group/{originalLiveId}/program", cancellationToken).ConfigureAwait(false);
                    var liveIdJson = await JsonDocument.ParseAsync(getLiveId, cancellationToken: cancellationToken).ConfigureAwait(false);
                    var liveIdRoot = liveIdJson.RootElement;
                    if (!liveIdRoot.GetProperty("meta").GetProperty("errorCode").GetString().Equals("OK")) throw new ChatReceivingException("コミュニティ・チャンネルが見つかりませんでした");
                    originalLiveId = liveIdRoot.GetProperty("data").GetProperty("nicoliveProgramId").GetString(); // lvから始まるLiveIDに置き換え

                }
                playerStatusStr = await httpClient.GetStreamAsync($"https://live2.nicovideo.jp/unama/watch/{originalLiveId}/programinfo", cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new LiveNotFoundChatReceivingException();
                if (e.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    throw new ChatReceivingException("ニコニコのサーバーがメンテナンス中の可能性があります");
                if (e.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    throw new ChatReceivingException("ニコニコのサーバーで内部エラーが発生しました");

                throw new ChatReceivingException("サーバーとの通信でエラーが発生しました", e);
            }

            var playerStatus = await JsonDocument.ParseAsync(playerStatusStr, cancellationToken: cancellationToken).ConfigureAwait(false);
            var playerStatusRoot = playerStatus.RootElement;

            if (playerStatusRoot.GetProperty("data").GetProperty("rooms").GetArrayLength() <= 0)
                throw new LiveNotFoundChatReceivingException();

            liveId = playerStatusRoot.GetProperty("data").GetProperty("socialGroup").GetProperty("id").GetString();
            

            try
            {
                await foreach (NiconicoUtils.NiconicoCommentXmlTag tag in commentReceiver.Receive(originalLiveId, cancellationToken))
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
                throw new ChatReceivingException("サーバーとの通信でエラーが発生しました", e);
            }
            catch (NiconicoUtils.ConnectionClosedNicoLiveCommentReceiverException e)
            {
                throw new ChatReceivingException("サーバーとの通信が切断されました", e);
            }
            catch (NiconicoUtils.ConnectionDisconnectNicoLiveCommentReceiverException)
            {
                throw new LiveClosedChatReceivingException();
            }
        }

        public async Task PostChat(BasicChatPostObject chatPostObject)
        {
            string liveId = this.liveId;
            if (liveId != "")
            {
                try { 
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
            using (commentReceiver)
            using (commentSender)
            using (httpClient)
            {
                if (cancellationTokenSource != null) cancellationTokenSource.Cancel();
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
