using CoreTweet;
using CoreTweet.Streaming;
using ObservableUtils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.ChatCollectServiceEntry;
using TVTComment.Model.NiconicoUtils;
using TVTComment.Model.TwitterUtils;
using TVTComment.Model.TwitterUtils.AnnictUtils;
using static TVTComment.Model.ChatCollectServiceEntry.TwitterLiveChatCollectServiceEntry.ChatCollectServiceCreationOption;

namespace TVTComment.Model.ChatCollectService
{
    class TwitterLiveChatCollectService : IChatCollectService
    {
        public class ChatPostObject : BasicChatPostObject
        {
            public string SuffixText { get; }
            public ChatPostObject(string text, string suffixtext) : base(text)
            {
                SuffixText = suffixtext;
            }
        }

        public string Name => "Twitterリアルタイム実況";

        public IChatCollectServiceEntry ServiceEntry { get; }

        public bool CanPost => true;

        private readonly TwitterAuthentication Twitter;
        private Task chatCollectTask;
        private readonly ConcurrentQueue<Status> statusQueue = new ConcurrentQueue<Status>();
        private CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly ObservableValue<string> SearchWord = new ObservableValue<string>("");
        private readonly ObservableValue<string> EventTextWord = new ObservableValue<string>("");
        private readonly SearchWordResolver SearchWordResolver;
        private readonly ModeSelectMethod ModeSelect;
        private readonly AnnictApis Annict;

        public TwitterLiveChatCollectService(IChatCollectServiceEntry serviceEntry, string searchWord, ModeSelectMethod modeSelect, SearchWordResolver searchWordResolver, TwitterAuthentication twitter)
        {
            Twitter = twitter;
            ServiceEntry = serviceEntry;
            SearchWord.Value = searchWord;
            ModeSelect = modeSelect;
            SearchWordResolver = searchWordResolver;
            switch (modeSelect)
            {
                case ModeSelectMethod.Preset:
                    SearchWord.Where(x => x != null && !x.Equals("")).Subscribe(res =>
                    {
                        if (!cancel.Token.IsCancellationRequested) cancel.Cancel();
                        cancel = new CancellationTokenSource();
                        chatCollectTask = SearchStreamAsync(res, cancel.Token);
                    });
                    break;

                case ModeSelectMethod.Manual:
                    chatCollectTask = SearchStreamAsync(searchWord, cancel.Token);
                    break;
                case ModeSelectMethod.Auto:
                    SearchWord.Where(x => x != null && !x.Equals("")).Subscribe(res =>
                    {
                        if (!cancel.Token.IsCancellationRequested) cancel.Cancel();
                        cancel = new CancellationTokenSource();
                        chatCollectTask = SearchStreamAsync(res, cancel.Token);
                    });
                    if (Twitter.AnnictToken != null && !Twitter.AnnictToken.Equals(""))
                    {
                        Annict = new(Twitter.AnnictToken);
                        EventTextWord.Where(x => x != null && !x.Equals("")).Subscribe(res => _ = annictSearch(EventTextWord.Value));
                    }
                    else
                    {
                        throw new ChatCollectServiceCreationException("AnnictAPIの認証設定がされていません");
                    }
                    break;
            }
        }

        public string GetInformationText()
        {
            var modename = new string[] { "自動(アニメ用・Annictからハッシュタグ取得)", "プリセット", "手動"};
            return $"検索モード:{modename[ModeSelect.GetHashCode()]}\n検索ワード:{SearchWord.Value}";
        }

        private async Task SearchStreamAsync(string searchWord, CancellationToken cancel)
        {
            await Task.Run(() =>
            {
                foreach (var status in Twitter.Token.Streaming.Filter(track: searchWord)
                            .OfType<StatusMessage>()
                            .Where(x => !x.Status.Text.StartsWith("RT"))
                            .Where(x => x.Status.Language is null or "und" || x.Status.Language.StartsWith("ja"))
                            .Select(x => x.Status))
                {
                    if (cancel.IsCancellationRequested || !SearchWord.Value.Equals(searchWord))
                        break;
                    statusQueue.Enqueue(status);
                }
            }, cancel);
        }

        private async Task annictSearch(string evetnText)
        {
            try
            {
                var result = await Annict.GetTwitterHashtagAsync(evetnText);
                SearchWord.Value = result != null && !result.Equals("") ? $"#{result}" : "";
            }
            catch (AnnictNotFoundResponseException)
            {
                SearchWord.Value = "";
            }
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel,EventInfo events, DateTime time)
        {
            if (ModeSelect == ModeSelectMethod.Preset || (SearchWord.Value != null && SearchWord.Value.Equals(""))) SearchWord.Value = SearchWordResolver.Resolve(channel.NetworkId, channel.ServiceId);
            if (ModeSelect == ModeSelectMethod.Auto)
            {
                var result = AnnimeTitleGetter.Convert(events.EventName);
                if (result != null && !result.Equals("")) EventTextWord.Value = result;
            }
            if (chatCollectTask != null)
            {
                if (chatCollectTask.IsCanceled)
                {
                    throw new ChatCollectException("Cancelしました");
                }
                if (chatCollectTask.IsFaulted)
                {
                    throw new ChatCollectException("TwitterのAPI制限に達したか問題が発生したため切断されました");
                }
            }

            var list = new List<Chat>();
            while (statusQueue.TryDequeue(out var status))
            {
                list.Add(ChatTwitterStatusToChat.Convert(status, Twitter.Token.ScreenName));
            }
            return list;
        }

        public async Task PostChat(BasicChatPostObject postObject)
        {
            try
            {
                var suffix = "\n" + (postObject as ChatPostObject)?.SuffixText ?? "";
                await Twitter.Token.Statuses.UpdateAsync(postObject.Text + suffix);
            }
            catch
            {
                throw new ChatPostException("ツイート投稿に失敗しました。\nTwitterAPIのApp permissionsがRead Onlyになっていないことを確認してください。");
            }
        }

        public void Dispose()
        {
            cancel.Cancel();
        }
    }
}
