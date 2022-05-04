using ObservableUtils;
using System;
using TVTComment.Model.ChatCollectService;
using TVTComment.Model.ChatService;
using TVTComment.Model.NiconicoUtils;
using TVTComment.Model.TwitterUtils;
using static TVTComment.Model.ChatCollectServiceEntry.TwitterLiveChatCollectServiceEntry.ChatCollectServiceCreationOption;

namespace TVTComment.Model.ChatCollectServiceEntry
{
    class TwitterLiveChatCollectServiceEntry : IChatCollectServiceEntry
    {
        public class ChatCollectServiceCreationOption : IChatCollectServiceCreationOption
        {
            public enum ModeSelectMethod { Auto, Preset, Manual }
            public string SearchWord { get; }
            public ModeSelectMethod Method;
            public ChatCollectServiceCreationOption(ModeSelectMethod method, string searchWord)
            {
                Method = method;
                SearchWord = searchWord;
            }
        }

        public IChatService Owner { get; }
        public string Id => "TwitterLive";
        public string Name => "Twitterリアルタイム実況";
        public string Description => "指定した検索ワードでTwitter実況";
        public bool CanUseDefaultCreationOption => true;

        private readonly ObservableValue<TwitterAuthentication> Session;
        private readonly SearchWordResolver searchWordResolver;

        public TwitterLiveChatCollectServiceEntry(TwitterChatService Owner, SearchWordResolver searchWordResolver, ObservableValue<TwitterAuthentication> Session)
        {
            this.Owner = Owner;
            this.searchWordResolver = searchWordResolver;
            this.Session = Session;
        }

        public IChatCollectService GetNewService(IChatCollectServiceCreationOption creationOption)
        {
            var annictmode = false;
            if (this.Owner is TwitterChatService twitter) annictmode = twitter.AnnictAutoEnable;
            creationOption ??= annictmode ? new ChatCollectServiceCreationOption(ModeSelectMethod.Auto, "") : new ChatCollectServiceCreationOption(ModeSelectMethod.Preset, "");
            if (creationOption == null || creationOption is not ChatCollectServiceCreationOption co)
                throw new ArgumentException($"Type of {nameof(creationOption)} must be {nameof(TwitterLiveChatCollectServiceEntry)}.{nameof(ChatCollectServiceCreationOption)}", nameof(creationOption));
            var session = Session.Value;
            if (session == null)
                throw new ChatCollectServiceCreationException("Twitterリアルタイム実況にはTwitterへのログインが必要です");
            return new TwitterLiveChatCollectService(this, co.SearchWord, co.Method, searchWordResolver, session);
        }
    }
}
