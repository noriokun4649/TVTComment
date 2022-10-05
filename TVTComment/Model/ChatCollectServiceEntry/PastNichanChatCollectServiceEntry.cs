using ObservableUtils;
using System;

namespace TVTComment.Model.ChatCollectServiceEntry
{
    class PastNichanChatCollectServiceEntry : IChatCollectServiceEntry
    {
        public ChatService.IChatService Owner { get; }

        public string Id => "2chPast";
        public string Name => "2ch過去ログ";
        public string Description => "2chの過去ログを自動で表示";

        public bool CanUseDefaultCreationOption => true;

        public PastNichanChatCollectServiceEntry(
            ChatService.NichanChatService chatService,
            NichanUtils.ThreadResolver threadResolver,
            ObservableValue<TimeSpan> backTime,
            ObservableValue<string> pastUserAgent
        )
        {
            Owner = chatService;
            this.threadResolver = threadResolver;
            this.backTime = backTime;
            this.pastUserAgent = pastUserAgent;
        }

        public ChatCollectService.IChatCollectService GetNewService(IChatCollectServiceCreationOption creationOption)
        {
            var threadSelector = new NichanUtils.AutoPastNichanThreadSelector(threadResolver, TimeSpan.FromSeconds(15), backTime.Value, pastUserAgent.Value);
            return new ChatCollectService.PastNichanChatCollectService(
                this, threadSelector, TimeSpan.FromSeconds(15), pastUserAgent.Value
            );
        }

        private readonly NichanUtils.ThreadResolver threadResolver;
        private readonly ObservableValue<TimeSpan> backTime;
        private readonly ObservableValue<string> pastUserAgent;
    }
}
