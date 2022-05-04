using CoreTweet;
using CoreTweet.Core;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using static CoreTweet.OAuth;

namespace TVTComment.Model.TwitterUtils
{
    class TwitterException : Exception
    {
        public TwitterException() { }
        public TwitterException(string message) : base(message) { }
        public TwitterException(string message, Exception inner) : base(message, inner) { }
        protected TwitterException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    class TwiiterAuthException : TwitterException
    {
        public TwiiterAuthException(string message) : base(message)
        {
        }
    }

    class TwitterAuthentication
    {
        private Tokens token = null;
        private string annicttoken = null;
        private OAuth2Token oAuth2Token = null;
        public bool IsLoggedin => token != null;
        private readonly string apikey;
        private readonly string apiseecret;
        private readonly string apiaccesstoken;
        private readonly string apiaccesssecret;
        private readonly string bearertoken;
        private readonly OAuthSession authSession;

        public Tokens Token
        {
            get
            {
                return token;
            }
        }
        public string AnnictToken
        {
            get
            {
                return annicttoken;
            }
        }
        public OAuth2Token OAuth2Token
        {
            get
            {
                return oAuth2Token;
            }
        }

        public OAuthSession AuthSession
        {
            get
            {
                return authSession;
            }
        }
        public TwitterAuthentication(string apikey, string apiseecret)
        {
            this.apikey = apikey;
            this.apiseecret = apiseecret;
            authSession = Authorize(apikey, apiseecret);
        }

        public TwitterAuthentication(string apikey, string apiseecret, string apiaccesstoken, string apiaccesssecret, string bearertoken)
        {
            this.apikey = apikey;
            this.apiseecret = apiseecret;
            this.apiaccesstoken = apiaccesstoken;
            this.apiaccesssecret = apiaccesssecret;
            this.bearertoken = bearertoken;
        }

        public void Login()
        {
            if (IsLoggedin)
                throw new InvalidOperationException("すでにログインしています");
            if (string.IsNullOrWhiteSpace(apiaccesssecret) && string.IsNullOrWhiteSpace(apiaccesstoken))
                throw new InvalidOperationException("Access tokenとAccess seecretが空白もしくは問題があります。");
            token = Tokens.Create(apikey, apiseecret, apiaccesstoken, apiaccesssecret);
            if (bearertoken != null && !bearertoken.Equals(""))
            {
                oAuth2Token = OAuth2Token.Create(apikey, apiseecret, bearertoken);
                Debug.WriteLine(oAuth2Token.BearerToken);
            }
        }

        public async Task Login(string pin)
        {
            if (IsLoggedin)
                throw new InvalidOperationException("すでにログインしています");
            if (string.IsNullOrWhiteSpace(pin))
                throw new InvalidOperationException("PINが空白もしくは問題があります。");
            token = await OAuth.GetTokensAsync(authSession, pin);
            oAuth2Token = await OAuth2.GetTokenAsync(apikey, apiseecret);
            if (oAuth2Token.BearerToken != null && !oAuth2Token.BearerToken.Equals(""))
            {
                oAuth2Token = OAuth2Token.Create(apikey, apiseecret, oAuth2Token.BearerToken);
                Debug.WriteLine(oAuth2Token.BearerToken);
            }
        }

        public void Logout()
        {
            if (!IsLoggedin)
                throw new InvalidOperationException("ログインしていません");
            token = null;
            oAuth2Token = null;
        }
        public void AnnictSet(string token)
        {
            annicttoken = token;
        }
    }
}
