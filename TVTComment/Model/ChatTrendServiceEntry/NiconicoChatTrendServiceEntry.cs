using System.Threading.Tasks;
using TVTComment.Model.ChatService;

namespace TVTComment.Model
{
    class NiconicoChatTrendServiceEntry : IChatTrendServiceEntry
    {
        public string Name => "ニコニコ実況";
        public string Description => "ニコニコ実況の勢いを表示します";

        private readonly NiconicoUtils.JkIdResolver jkIdResolver;
        private readonly NiconicoChatServiceSettings chatServiceSettings;

        public NiconicoChatTrendServiceEntry(NiconicoUtils.JkIdResolver jkIdResolver,NiconicoChatServiceSettings chatServiceSettings)
        {
            this.jkIdResolver = jkIdResolver;
            this.chatServiceSettings = chatServiceSettings;
        }

        public Task<IChatTrendService> GetNewService()
        {
            return Task.FromResult((IChatTrendService)new NiconicoChatTrendService(jkIdResolver, chatServiceSettings));
        }
    }
}
