using ObservableUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TVTComment.Model.ChatService
{
    class NiconicoChatServiceSettings
    {
        public string UserId { get; set; } = "";
        public string Password { get; set; } = "";
        public bool Always184 { get; set; } = true;
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

        public string UserId
        {
            get { return settings.UserId; }
        }
        public string UserPassword
        {
            get { return settings.Password; }
        }
        public bool Always184
        {
            get { return settings.Always184; }
            set { settings.Always184 = value; }
        }
        public bool IsLoggedin { get; private set; }

        public NiconicoChatService(
            NiconicoChatServiceSettings settings, ChannelDatabase channelDatabase,
            string jikkyouIdTableFilePath, string liveIdTableFilePath
        )
        {
            this.settings = settings; this.settings = settings;
            jkIdResolver = new NiconicoUtils.JkIdResolver(channelDatabase, new NiconicoUtils.JkIdTable(jikkyouIdTableFilePath));
            liveIdResolver = new NiconicoUtils.LiveIdResolver(channelDatabase, new NiconicoUtils.LiveIdTable(liveIdTableFilePath));

            try
            {
                if (!string.IsNullOrWhiteSpace(UserId) && !string.IsNullOrWhiteSpace(UserPassword))
                    SetUser(UserId, UserPassword).Wait();
            }
            catch (AggregateException e)
            when (e.InnerExceptions.Count == 1 && e.InnerExceptions[0] is NiconicoUtils.NiconicoLoginSessionException)
            { }

            ChatCollectServiceEntries = new ChatCollectServiceEntry.IChatCollectServiceEntry[] {
                new ChatCollectServiceEntry.NewNiconicoJikkyouChatCollectServiceEntry(this, liveIdResolver, loginSession),
                new ChatCollectServiceEntry.NiconicoLiveChatCollectServiceEntry(this, loginSession),
                new ChatCollectServiceEntry.TsukumijimaJikkyoApiChatCollectServiceEntry(this, jkIdResolver, loginSession)
            };
            ChatTrendServiceEntries = new IChatTrendServiceEntry[] { new NewNiconicoChatTrendServiceEntry(liveIdResolver, loginSession) };
        }

        /// <summary>
        /// ニコニコのユーザーを指定しログインする
        /// 失敗した場合オブジェクトの状態は変化しない
        /// </summary>
        /// <param name="userId">ニコニコのユーザーID</param>
        /// <param name="userPassword">ニコニコのパスワード</param>
        /// <exception cref="ArgumentException"><paramref name="userId"/>または<paramref name="userPassword"/>がnull若しくはホワイトスペースだった時</exception>
        /// <exception cref="NiconicoUtils.NiconicoLoginSessionException">ログインに失敗した時</exception>
        public async Task SetUser(string userId, string userPassword, string onetimePassword = "")
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException($"{nameof(userId)} must not be null nor white space", nameof(userId));
            if (string.IsNullOrWhiteSpace(userPassword))
                throw new ArgumentException($"{nameof(userPassword)} must not be null nor white space", nameof(userPassword));

            //ログインしてみる
            var tmpSession = new NiconicoUtils.NiconicoLoginSession(userId, userPassword, onetimePassword);
            await tmpSession.Login().ConfigureAwait(false);

            //成功したら設定、セッションを置き換える
            IsLoggedin = true;
            settings.UserId = userId;
            settings.Password = userPassword;
            try
            {
                await (loginSession.Value?.Logout() ?? Task.CompletedTask);
            }
            catch (NiconicoUtils.NiconicoLoginSessionException)
            { }
            loginSession.Value = tmpSession;
        }

        public void Dispose()
        {
            if(this.loginSession.Value?.IsLoggedin ?? false)
            {
                try
                {
                    this.loginSession.Value.Logout().Wait();
                }
                catch (AggregateException e) when (e.InnerExceptions.All(
                    x => x is NiconicoUtils.NiconicoLoginSessionException
                ))
                {
                }
            }
        }
    }
}
