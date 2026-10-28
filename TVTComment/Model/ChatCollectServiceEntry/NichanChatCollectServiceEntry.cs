using ObservableUtils;
using System;

namespace TVTComment.Model.ChatCollectServiceEntry
{
    abstract class NichanChatCollectServiceEntry : IChatCollectServiceEntry
    {
        public class ChatCollectServiceCreationOption : IChatCollectServiceCreationOption
        {
            /// <summary>
            /// スレッドの選択方法を表す
            /// </summary>
            public enum ThreadSelectMethod
            {
                /// <summary>
                /// <seealso cref="Uri"/>で指定されたスレッドを固定で表示
                /// </summary>
                Fixed,
                /// <summary>
                /// <seealso cref="Uri"/>で指定された板にある<seealso cref="Keywords"/>を含んだタイトルのスレッドを表示
                /// </summary>
                Keyword,
                /// <summary>
                /// <seealso cref="Uri"/>で指定された板にある<seealso cref="Title"/>と似たタイトルのスレッドを表示
                /// </summary>
                Fuzzy,
                /// <summary>
                /// チャンネル情報から自動で決めたスレッドを表示
                /// </summary>
                Auto,
            }

            public ThreadSelectMethod Method { get; }
            public Uri Uri { get; }
            public string Title { get; }
            public string[] Keywords { get; }

            public ChatCollectServiceCreationOption(ThreadSelectMethod method, Uri uri, string title, string[] keywords)
            {
                Method = method;
                Uri = uri;
                Title = title;
                Keywords = keywords;
            }
        }

        public ChatService.IChatService Owner { get; }
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public bool CanUseDefaultCreationOption => true;

        protected ObservableValue<TimeSpan> resCollectInterval;
        protected ObservableValue<TimeSpan> threadSearchInterval;
        protected NichanUtils.ThreadResolver threadResolver;

        public NichanChatCollectServiceEntry(
            ChatService.NichanChatService owner,
            ObservableValue<TimeSpan> resCollectInterval, ObservableValue<TimeSpan> threadSearchInterval,
            NichanUtils.ThreadResolver threadResolver
        )
        {
            Owner = owner;
            this.resCollectInterval = resCollectInterval;
            this.threadSearchInterval = threadSearchInterval;
            this.threadResolver = threadResolver;
        }

        public ChatCollectService.IChatCollectService GetNewService(IChatCollectServiceCreationOption creationOption)
        {
            creationOption ??= new ChatCollectServiceCreationOption(ChatCollectServiceCreationOption.ThreadSelectMethod.Auto, null, null, null);

            if (creationOption is not ChatCollectServiceCreationOption co)
                throw new ArgumentException($"Type of {nameof(creationOption)} must be {nameof(NichanChatCollectServiceEntry)}.{nameof(ChatCollectServiceCreationOption)}", nameof(creationOption));

            NichanUtils.INichanThreadSelector selector = co.Method switch
            {
                ChatCollectServiceCreationOption.ThreadSelectMethod.Fixed => new NichanUtils.FixedNichanThreadSelector(new string[] { co.Uri.ToString() }),
                ChatCollectServiceCreationOption.ThreadSelectMethod.Keyword => new NichanUtils.KeywordNichanThreadSelector(co.Uri.ToString(), co.Keywords),
                ChatCollectServiceCreationOption.ThreadSelectMethod.Fuzzy => new NichanUtils.FuzzyNichanThreadSelector(co.Uri.ToString(), co.Title),
                ChatCollectServiceCreationOption.ThreadSelectMethod.Auto => new NichanUtils.AutoNichanThreadSelector(threadResolver),
                _ => throw new ArgumentOutOfRangeException(nameof(creationOption), "((ChatCollectServiceCreationOption)creationOption).Method is out of range"),
            };

            return GetNichanChatCollectService(selector);
        }

        protected abstract ChatCollectService.IChatCollectService GetNichanChatCollectService(
            NichanUtils.INichanThreadSelector threadSelector
        );
    }

    class HTMLNichanChatCollectServiceEntry : NichanChatCollectServiceEntry
    {
        public override string Id => "2chHTML";
        public override string Name => "2chHTML";
        public override string Description => "5chのレスをHTMLのスクレイピングで表示";


        public HTMLNichanChatCollectServiceEntry(
            ChatService.NichanChatService owner,
            ObservableValue<TimeSpan> resCollectInterval,
            ObservableValue<TimeSpan> threadSearchInterval,
            NichanUtils.ThreadResolver threadResolver
        ) : base(owner, resCollectInterval, threadSearchInterval, threadResolver)
        {
        }

        protected override ChatCollectService.IChatCollectService GetNichanChatCollectService(NichanUtils.INichanThreadSelector threadSelector)
        {
            return new ChatCollectService.HTMLNichanChatCollectService(
                this, resCollectInterval.Value,
                threadSearchInterval.Value, threadSelector
            );
        }
    }

    class DATNichanChatCollectServiceEntry : NichanChatCollectServiceEntry
    {
        public override string Id => "2chDAT";
        public override string Name => "2chDAT";
        public override string Description => "5chの公開DATを直接取得して表示";

        private readonly ObservableValue<Nichan.ApiClient> apiClient;

        public DATNichanChatCollectServiceEntry(
            ChatService.NichanChatService owner,
            ObservableValue<TimeSpan> resCollectInterval,
            ObservableValue<TimeSpan> threadSearchInterval,
            NichanUtils.ThreadResolver threadResolver,
            ObservableValue<Nichan.ApiClient> nichanApiClient
        ) : base(owner, resCollectInterval, threadSearchInterval, threadResolver)
        {
            apiClient = nichanApiClient;
        }

        protected override ChatCollectService.IChatCollectService GetNichanChatCollectService(NichanUtils.INichanThreadSelector threadSelector)
        {
            return new ChatCollectService.DATNichanChatCollectService(
                this, resCollectInterval.Value,
                threadSearchInterval.Value, threadSelector, apiClient.Value
            );
        }
    }
}
