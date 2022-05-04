using ObservableUtils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TVTComment.Model.ChatCollectServiceEntry;
using TVTComment.Model.NiconicoUtils;
using TVTComment.Model.TwitterUtils;

namespace TVTComment.Model.ChatService
{
    class TwitterChatServiceSettings
    {
        public string ApiKey { get; set; } = "";
        public string ApiSecret { get; set; } = "";
        public string BearerToken { get; set; } = "";
        public string ApiAccessToken { get; set; } = "";
        public string ApiAccessSecret { get; set; } = "";
        public string AnnictAccessToken { get; set; } = "";
        public bool AnnictAutoEnable { get; set; } = false;
    }

    class TwitterChatService : IChatService
    {
        public string Name => "Twitter";
        public IReadOnlyList<IChatCollectServiceEntry> ChatCollectServiceEntries { get; }
        public IReadOnlyList<IChatTrendServiceEntry> ChatTrendServiceEntries { get; }

        private readonly ObservableValue<TwitterAuthentication> twitterSession = new ObservableValue<TwitterAuthentication>();
        private readonly TwitterChatServiceSettings settings;
        private readonly SearchWordResolver searchWordResolver;

        public bool IsLoggedin { get; private set; }
        public string UserName { get; private set; }

        public string ApiKey
        {
            get { return settings.ApiKey; }
            set { settings.ApiKey = value; }
        }
        public string ApiSecret
        {
            get { return settings.ApiSecret; }
            set { settings.ApiSecret = value; }
        }
        public string BearerToken
        {
            get { return settings.BearerToken; }
            set { settings.BearerToken = value; }
        }
        public string ApiAccessToken
        {
            get { return settings.ApiAccessToken; }
        }
        public string ApiAccessSecret
        {
            get { return settings.ApiAccessSecret; }
        }
        public string AnnictAccessToken
        {
            get { return settings.AnnictAccessToken; }
        }

        public bool AnnictAutoEnable
        {
            get { return settings.AnnictAutoEnable; }
            set { settings.AnnictAutoEnable = value; }
        }

        public TwitterChatService(TwitterChatServiceSettings settings, ChannelDatabase channelDatabase, string filepath)
        {
            this.settings = settings;
            var searchWordTable = new SearchWordTable(filepath);
            searchWordResolver = new SearchWordResolver(channelDatabase, searchWordTable);
            try
            {
                if (!string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret) &&
                    !string.IsNullOrWhiteSpace(ApiAccessToken) && !string.IsNullOrWhiteSpace(ApiAccessSecret))
                    LoginAccessTokens(ApiKey, ApiSecret, ApiAccessToken, ApiAccessSecret, BearerToken).Wait();
            }
            catch (AggregateException e)
            when (e.InnerExceptions.Count == 1 && e.InnerExceptions[0] is TwiiterAuthException)
            { }

            ChatCollectServiceEntries = new IChatCollectServiceEntry[2] {
                new TwitterLiveChatCollectServiceEntry(this, searchWordResolver, twitterSession),
                new TwitterLiveV2ChatCollectServiceEntry(this, searchWordResolver, twitterSession),
            };
            ChatTrendServiceEntries = Array.Empty<IChatTrendServiceEntry>();
        }

        /// <summary>
        /// TwitterにAccessTokenでログインする
        /// 失敗した場合オブジェクトの状態は変化しない
        /// </summary>
        /// <param name="apiKey">TwitterAPIのKey</param>
        /// <param name="apiSecret">TwitterAPIのSecret</param>
        /// <param name="apiAccessToken">TwitterAPIのAccessSecret</param>
        /// <param name="apiAccessSecret">TwitterAPIのAccessSecret</param>
        /// <exception cref="ArgumentException"><paramref name="apiKey"/>または<paramref name="apiSecret"/>または<paramref name="apiAccessToken"/>または<paramref name="apiAccessSecret"/>がnull若しくはホワイトスペースだった時</exception>
        /// <exception cref="TwiiterAuthException">TwitterAPIで認証に失敗した時</exception>
        public async Task LoginAccessTokens(string apiKey, string apiSecret, string apiAccessToken, string apiAccessSecret, string bearerToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException($"{nameof(apiKey)} が空白もしくは不正です", nameof(apiKey));
            if (string.IsNullOrWhiteSpace(apiSecret))
                throw new ArgumentException($"{nameof(apiSecret)} が空白もしくは不正です", nameof(apiSecret));
            if (string.IsNullOrWhiteSpace(apiAccessToken))
                throw new ArgumentException($"{nameof(apiAccessToken)} が空白もしくは不正です", nameof(apiAccessToken));
            if (string.IsNullOrWhiteSpace(apiAccessSecret))
                throw new ArgumentException($"{nameof(apiAccessSecret)} が空白もしくは不正です", nameof(apiAccessSecret));

            var twitterAuthentication = new TwitterAuthentication(apiKey, apiSecret, apiAccessToken, apiAccessSecret, bearerToken);
            twitterAuthentication.Login();
            var tokens = twitterAuthentication.Token;
            var userResponse = await tokens.Account.VerifyCredentialsAsync().ConfigureAwait(false);
            IsLoggedin = true;
            UserName = userResponse.Name;
            settings.ApiKey = apiKey;
            settings.ApiSecret = apiSecret;
            settings.ApiAccessToken = apiAccessToken;
            settings.ApiAccessSecret = apiAccessSecret;
            settings.BearerToken = bearerToken;
            twitterSession.Value = twitterAuthentication;
            twitterSession.Value.AnnictSet(settings.AnnictAccessToken);
        }
        public TwitterAuthentication InitOAuthPin(string apiKey, string apiSecret)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException($"{nameof(apiKey)} が空白もしくは不正です", nameof(apiKey));
            if (string.IsNullOrWhiteSpace(apiSecret))
                throw new ArgumentException($"{nameof(apiSecret)} が空白もしくは不正です", nameof(apiSecret));
            var twitterAuthentication = new TwitterAuthentication(apiKey, apiSecret);
            settings.ApiKey = apiKey;
            settings.ApiSecret = apiSecret;
            return twitterAuthentication;
        }

        /// <summary>
        /// TwitterにOAuthのPinでログインする
        /// 失敗した場合オブジェクトの状態は変化しない
        /// </summary>
        /// <param name="twitterAuthentication">TwitterAuthenticationのインスタンス</param>
        /// <param name="pin">OAuthのPINコード</param>
        /// <exception cref="ArgumentException"><paramref name="pin"/>がnull若しくはホワイトスペースだった時</exception>
        /// <exception cref="TwiiterAuthException">TwitterAPIで認証に失敗した時</exception>
        /// <exception cref="TwiiterException">TwitterAPIで認証に失敗した時</exception>
        public async Task LoginOAuthPin(TwitterAuthentication twitterAuthentication, string pin)
        {
            if (string.IsNullOrWhiteSpace(pin))
                throw new ArgumentException($"{nameof(pin)} が空白もしくは不正です", nameof(pin));
            if (twitterAuthentication == null)
                throw new TwiiterAuthException("まずは「OAuth画面を開く」を押してPINコードを表示させてください");
            await twitterAuthentication.Login(pin).ConfigureAwait(false);
            var tokens = twitterAuthentication.Token;
            var userResponse = await tokens.Account.VerifyCredentialsAsync().ConfigureAwait(false);
            IsLoggedin = true;
            UserName = userResponse.Name;
            twitterSession.Value = twitterAuthentication;
            twitterSession.Value.AnnictSet(settings.AnnictAccessToken);
            settings.ApiAccessToken = tokens.AccessToken;
            settings.ApiAccessSecret = tokens.AccessTokenSecret;
            settings.BearerToken = twitterAuthentication.OAuth2Token.BearerToken;
        }

        public void Logout()
        {
            twitterSession.Value.Logout();
            twitterSession.Value = null;
            IsLoggedin = false;
            settings.ApiAccessToken = "";
            settings.ApiAccessSecret = "";
            settings.BearerToken = "";
        }

        public void SetAnnictToken(string token)
        {
            if (twitterSession.Value != null) { 
                settings.AnnictAccessToken = token;
                twitterSession.Value.AnnictSet(token);
            }
            else
            {
                throw new ArgumentException($"先にTwitterへのログインが必要です。");
            }
        }

        public void Dispose()
        {
        }
    }
}
