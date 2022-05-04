using ObservableUtils;
using System.Threading.Tasks;
using TVTComment.Model.ChatTrendService;

namespace TVTComment.Model
{
    class NewNiconicoChatTrendServiceEntry : IChatTrendServiceEntry
    {
        public string Name => "新ニコニコ実況";
        public string Description => "ニコニコ実況の勢いを表示します";

        private readonly NiconicoUtils.LiveIdResolver liveIdResolver;
        private readonly ObservableValue<NiconicoUtils.NiconicoLoginSession> session;

        public NewNiconicoChatTrendServiceEntry(NiconicoUtils.LiveIdResolver liveIdResolver, ObservableValue<NiconicoUtils.NiconicoLoginSession> session)
        {
            this.liveIdResolver = liveIdResolver;
            this.session = session;
        }

        public Task<IChatTrendService> GetNewService()
        {
            return Task.FromResult((IChatTrendService)new NewNiconicoChatTrendService(liveIdResolver, session != null ? session.Value : null));
        }
    }
}
