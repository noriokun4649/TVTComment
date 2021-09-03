﻿using CoreTweet.V2;
using ObservableUtils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.ChatCollectServiceEntry;
using TVTComment.Model.NiconicoUtils;
using TVTComment.Model.TwitterUtils;
using static TVTComment.Model.ChatCollectServiceEntry.TwitterLiveChatCollectServiceEntry.ChatCollectServiceCreationOption;

namespace TVTComment.Model.ChatCollectService
{
    class TwitterLiveV2ChatCollectService : IChatCollectService
    {
        public class ChatPostObject : BasicChatPostObject
        {
            public string SuffixText { get; }
            public ChatPostObject(string text, string suffixtext) : base(text)
            {
                SuffixText = suffixtext;
            }
        }

        public string Name => "Twitterリアルタイム実況 v2 API版";

        public IChatCollectServiceEntry ServiceEntry { get; }

        public bool CanPost => true;

        private readonly TwitterAuthentication Twitter;
        private Task chatCollectTask;
        private readonly ConcurrentQueue<FilterStreamResponse> statusQueue = new();
        private readonly CancellationTokenSource cancel = new();
        private readonly ObservableValue<string> SearchWord = new("");
        private readonly SearchWordResolver SearchWordResolver;
        private readonly ModeSelectMethod ModeSelect;


        public TwitterLiveV2ChatCollectService(IChatCollectServiceEntry serviceEntry, string searchWord, ModeSelectMethod modeSelect, SearchWordResolver searchWordResolver, TwitterAuthentication twitter)
        {
            ServiceEntry = serviceEntry;
            Twitter = twitter;
            ModeSelect = modeSelect;
            SearchWordResolver = searchWordResolver;
            if (Twitter.OAuth2Token == null)
                throw new ChatCollectServiceCreationException("Twitter APIのBearerTokenが不明なためTwitter API v2 エンドポイントは利用できません。");

            try
            {
                DeleteOldSearchWords();
            }
            catch (CoreTweet.TwitterException e)
            {
                throw new ChatCollectServiceCreationException($"このAPI KeyでTwitter API v2 エンドポイントが利用できないか\nTwitterのAPI制限に達したもしくは問題が発生したため切断されました\n\n{e.Message}");
            }

            SearchWord.Where(x => x != null && !x.Equals("")).Subscribe(res =>
            {
                DeleteOldSearchWords();
                var filter = new FilterRule();
                filter.Value = res;
                filter.Tag = "TvTComment";
                Twitter.OAuth2Token.V2.FilteredStreamApi.CreateRules(add: new[] { filter });

            });
            SearchWord.Value = searchWord;
            chatCollectTask = SearchStreamAsync(cancel.Token);
        }

        public string GetInformationText()
        {
            return $"検索モード:{ModeSelect}\n検索ワード:{SearchWord.Value}";
        }

        private async Task SearchStreamAsync(CancellationToken cancel)
        {
            await Task.Run(() =>
            {
                foreach (var status in Twitter.OAuth2Token.V2.FilteredStreamApi.Filter(expansions: TweetExpansions.AuthorId, tweet_fields: TweetFields.CreatedAt)
                    .StreamAsEnumerable().OfType<FilterStreamResponse>())
                {
                    if (cancel.IsCancellationRequested)
                        break;
                    statusQueue.Enqueue(status);
                }
            }, cancel);
        }

        public IEnumerable<Chat> GetChats(ChannelInfo channel,EventInfo eventInfo, DateTime time)
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
                    throw new ChatCollectException("このAPI KeyでTwitter API v2 エンドポイントが利用できないか\nTwitterのAPI制限に達したもしくは問題が発生したため切断されました");
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

        private void DeleteOldSearchWords()
        {
            try
            {
                var old = Twitter.OAuth2Token.V2.FilteredStreamApi.GetRules().Data.Where(res => res.Tag == "TvTComment").Select(res => res.Value);
                Twitter.OAuth2Token.V2.FilteredStreamApi.DeleteRules(values: old);
            }
            catch (ArgumentNullException e)
            {
                Console.WriteLine(e);
            }
        }

        public void Dispose()
        {
            try
            {
                DeleteOldSearchWords();
            }
            catch (CoreTweet.TwitterException e)
            {
                Console.WriteLine(e);
            }
            cancel.Cancel();
        }
    }
}