using ObservableUtils;
using System;
using TVTComment.Model.ChatCollectService;

namespace TVTComment.Model.ChatCollectServiceEntry
{
    class NxJikkyoChatCollectServiceEntry : IChatCollectServiceEntry
    {
        public class ChatCollectServiceCreationOption : IChatCollectServiceCreationOption
        {
        }

        public ChatService.IChatService Owner { get; }
        public string Id => "NXJikkyo";
        public string Name => "NX-Jikkyo by tsukumi";
        public string Description => "ニコニコ実況民のための避難所";
        public bool CanUseDefaultCreationOption => true;

        private readonly NiconicoUtils.JkIdResolver jkIdResolver;

        public NxJikkyoChatCollectServiceEntry(
            ChatService.NiconicoChatService owner, NiconicoUtils.JkIdResolver jkIdResolver
        )
        {
            Owner = owner;
            this.jkIdResolver = jkIdResolver;
        }

        public IChatCollectService GetNewService(IChatCollectServiceCreationOption creationOption)
        {
            if (creationOption != null && !(creationOption is ChatCollectServiceCreationOption))
                throw new ArgumentException($"Type of {nameof(creationOption)} must be {nameof(NxJikkyoChatCollectService)}.{nameof(ChatCollectServiceCreationOption)}", nameof(creationOption));
            return new NxJikkyoChatCollectService(this, jkIdResolver);
        }
    }
}
