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
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly ObservableValue<string> SearchWord = new ObservableValue<string>("");
        private readonly SearchWordResolver SearchWordResolver;
        private readonly ModeSelectMethod ModeSelect;
        private bool isStreaming = true;
        private long? lastStatusId;

        public TwitterLiveChatCollectService(IChatCollectServiceEntry serviceEntry, string searchWord, ModeSelectMethod modeSelect, SearchWordResolver searchWordResolver, TwitterAuthentication twitter)
        {
            ServiceEntry = serviceEntry;
            SearchWord.Value = searchWord;
            Twitter = twitter;
            ModeSelect = modeSelect;
            SearchWordResolver = searchWordResolver;
            switch (modeSelect)
            {
                case ModeSelectMethod.Auto:
                    SearchWord.Where(x => x != null && !x.Equals("")).Subscribe(res => chatCollectTask = SearchAsync(res, cancel.Token));
                    break;

                case ModeSelectMethod.Manual:
                    chatCollectTask = SearchAsync(searchWord, cancel.Token);
                    break;
            }
        }

        public string GetInformationText()
        {
            return $"検索モード:{ModeSelect}\n検索ワード:{SearchWord.Value}\n取得方法:{(isStreaming ? "ストリーミング API" : "REST API")}";
        }

        private async Task SearchAsync(string searchWord, CancellationToken token)
        {
            await Task.Run(() =>
            {
                try
                {
                    isStreaming = true;
                    SearchStream(searchWord, token);
                }
                catch (Exception)
                {
                    isStreaming = false;
                    lastStatusId = null;
                    SearchTweets(searchWord, token);
                }
            }, token);
        }

        private void SearchStream(string searchWord, CancellationToken token)
        {
            foreach (var status in Twitter.Token.Streaming.Filter(track: searchWord)
                        .OfType<StatusMessage>()
                        .Where(x => !x.Status.Text.StartsWith("RT"))
                        .Where(x => x.Status.Language is null or "und" || x.Status.Language.StartsWith("ja"))
                        .Select(x => x.Status))
            {
                if (token.IsCancellationRequested || !SearchWord.Value.Equals(searchWord))
                    break;
                statusQueue.Enqueue(status);
            }
        }

        private void SearchTweets(string searchWord, CancellationToken token)
        {
            while (!token.IsCancellationRequested && SearchWord.Value.Equals(searchWord))
            {
                var response = Twitter.Token.Search.Tweets(q: searchWord, since_id: lastStatusId);
                var tweets = response.Where(x => !x.Text.StartsWith("RT"))
                    .Where(x => x.Language is null or "und" || x.Language.StartsWith("ja"))
                    .ToList();

                foreach (var tweet in tweets)
                {
                    statusQueue.Enqueue(tweet);
                }
                lastStatusId = tweets.FirstOrDefault()?.Id + 1 ?? lastStatusId;

                if (response.RateLimit.Remaining == 0)
                {
                    Task.Delay(15, token);
                }
                else
                {
                    var duration = DateTime.Now - response.RateLimit.Reset;
                    var safeRate = duration / response.RateLimit.Remaining;
                    Task.Delay(safeRate, token);
                }
            }
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel, DateTime time)
        {
            if (ModeSelect == ModeSelectMethod.Auto)
            {

                SearchWord.Value = SearchWordResolver.Resolve(channel.NetworkId, channel.ServiceId);
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
                list.Add(ChatTwitterStatusToChat.Convert(status));
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
