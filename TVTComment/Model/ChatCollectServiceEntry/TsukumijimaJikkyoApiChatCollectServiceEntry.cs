using ObservableUtils;

namespace TVTComment.Model.ChatCollectServiceEntry
{
    class TsukumijimaJikkyoApiChatCollectServiceEntry : IChatCollectServiceEntry
    {
        public ChatService.IChatService Owner { get; }
        public string Id => "TsukumijimaJikkyoApi";
        public string Name => "非公式ニコニコ実況過去ログ";
        public string Description => "tsukumijimaさんが提供しているニコニコ実況の過去ログAPIからコメントを表示";
        public bool CanUseDefaultCreationOption => true;
        private readonly ObservableValue<NiconicoUtils.NiconicoLoginSession> session;


        public TsukumijimaJikkyoApiChatCollectServiceEntry(
            ChatService.IChatService chatService,
            NiconicoUtils.JkIdResolver jkIdResolver,
            ObservableValue<NiconicoUtils.NiconicoLoginSession> loginSession
        )
        {
            Owner = chatService;
            this.jkIdResolver = jkIdResolver;
            session = loginSession;
        }

        public ChatCollectService.IChatCollectService GetNewService(IChatCollectServiceCreationOption creationOption)
        {
            return new ChatCollectService.TsukumijimaJikkyoApiChatCollectService(this, jkIdResolver, session.Value);
        }

        private readonly NiconicoUtils.JkIdResolver jkIdResolver;
    }
}
