using ObservableUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TVTComment.Model.NiconicoUtils;

namespace TVTComment.Model.ChatService
{
    class NiconicoChatServiceSettings
    {
        public string OAuthToken { get; set; } = string.Empty;
    }

    class NiconicoChatService : IChatService
    {
        public string Name => "ニコニコ";
        public IReadOnlyList<ChatCollectServiceEntry.IChatCollectServiceEntry> ChatCollectServiceEntries { get; }
        public IReadOnlyList<IChatTrendServiceEntry> ChatTrendServiceEntries { get; }

        //このChatServiceに行われた設定変更が子のChatServiceEntryに伝わるようにするためにObservableValueで包む
        //private ObservableValue<Dictionary<uint, int>> jkIdTable = new ObservableValue<Dictionary<uint, int>>();
        private readonly ObservableValue<NiconicoUtils.NiconicoLoginSession> loginSession = new ObservableValue<NiconicoUtils.NiconicoLoginSession>();

        private readonly NiconicoUtils.LiveIdResolver liveIdResolver;
        private readonly NiconicoUtils.JkIdResolver jkIdResolver;
        private readonly NiconicoChatServiceSettings settings;

        public string OAuthToken
        {
            get { return settings.OAuthToken; }
            set { settings.OAuthToken = value; }
        }
        public bool IsAuthorization { get; private set; }

        public NiconicoChatService(
            NiconicoChatServiceSettings settings, ChannelDatabase channelDatabase,
            string jikkyouIdTableFilePath, string liveIdTableFilePath
        )
        {
            this.settings = settings;
            jkIdResolver = new NiconicoUtils.JkIdResolver(channelDatabase, new NiconicoUtils.JkIdTable(jikkyouIdTableFilePath));
            liveIdResolver = new NiconicoUtils.LiveIdResolver(channelDatabase, new NiconicoUtils.LiveIdTable(liveIdTableFilePath));

            try
            {
                if (!string.IsNullOrWhiteSpace(OAuthToken))
                    SetToken(OAuthToken).ConfigureAwait(false);
            }
            catch (AggregateException e)
            when (e.InnerExceptions.Count == 1 && e.InnerExceptions[0] is NiconicoUtils.NiconicoLoginSessionException)
            { }

            ChatCollectServiceEntries = new ChatCollectServiceEntry.IChatCollectServiceEntry[] {
                new ChatCollectServiceEntry.NewNiconicoJikkyouChatCollectServiceEntry(this, liveIdResolver, loginSession),
                new ChatCollectServiceEntry.NiconicoLiveChatCollectServiceEntry(this, loginSession),
                new ChatCollectServiceEntry.TsukumijimaJikkyoApiChatCollectServiceEntry(this, jkIdResolver)
            };
            ChatTrendServiceEntries = new IChatTrendServiceEntry[] { new NewNiconicoChatTrendServiceEntry(liveIdResolver, loginSession) };
        }

        /// <summary>
        /// ニコニコの認可トークンを指定し検証する
        /// 失敗した場合オブジェクトの状態は変化しない
        /// </summary>
        /// <param name="token">ニコニコの認可トークン</param>
        /// <exception cref="ArgumentException"><paramref name="token"/>がnull若しくはホワイトスペースだった時</exception>
        /// <exception cref="NiconicoUtils.NiconicoLoginSessionException">認可トークンの検証に失敗した時</exception>
        public async Task SetToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException($"{nameof(token)} must not be null nor white space", nameof(token));
            loginSession.Value = new NiconicoLoginSession();
            //検証してみる
            await loginSession.Value.Authorization(token).ConfigureAwait(false);

            //成功したら設定
            IsAuthorization = true;
            OAuthToken = token;
        }

        public void Dispose()
        {
        }
    }
}
