using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TVTComment.Model.NiconicoUtils
{
    /// <summary>
    /// <see cref="NiconicoLoginSession"/>で投げられる例外
    /// </summary>
    [System.Serializable]
    class NiconicoLoginSessionException : Exception
    {
        public NiconicoLoginSessionException() { }
        public NiconicoLoginSessionException(string message) : base(message) { }
        public NiconicoLoginSessionException(string message, Exception inner) : base(message, inner) { }
        protected NiconicoLoginSessionException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// ログインに失敗した
    /// </summary>
    class LoginFailureNiconicoLoginSessionException : NiconicoLoginSessionException
    { }

    /// <summary>
    /// ネットワークエラーが発生した
    /// </summary>
    class NetworkNiconicoLoginSessionException : NiconicoLoginSessionException
    {
        public NetworkNiconicoLoginSessionException(Exception inner) : base(null, inner)
        { }
    }

    class NiconicoLoginSession
    {
        private readonly string mail;
        private readonly string password;
        private CookieCollection cookie = null;
        private string token = null;
        private string userid = null;

        public bool IsLoggedin => cookie != null;
        public bool IsAuthorization => token != null;
        /// <summary>
        /// 送信するべき認証情報を含んだクッキー
        /// </summary>
        /// <exception cref="InvalidOperationException">ログインしていない</exception>
        public CookieCollection Cookie
        {
            get
            {
                if (IsLoggedin)
                    return cookie;
                else
                    throw new InvalidOperationException("ログインしていません");
            }
        }

        public string UserId
        {
            get
            {
                if (userid != null)
                    return userid;
                else
                    throw new InvalidOperationException("UserIDが取得できてません");
            }
        }

        public string Token
        {
            get
            {
                if (IsAuthorization)
                    return token;
                else
                    throw new InvalidOperationException("認可トークンがありません\n設定からOAuth認可を行ってください");
            }
        }

        public NiconicoLoginSession(string mail, string password)
        {
            this.mail = mail;
            this.password = password;
        }

        /// <summary>
        /// ログインする
        /// </summary>
        /// <exception cref="InvalidOperationException">すでにログインしている</exception>
        /// <exception cref="LoginFailureNiconicoLoginSessionException"></exception>
        /// <exception cref="NetworkNiconicoLoginSessionException"></exception>
        public async Task Login()
        {
            if (IsLoggedin)
                throw new InvalidOperationException("すでにログインしています");

            const string loginUrl = "https://secure.nicovideo.jp/secure/login?site=niconico";

            var handler = new HttpClientHandler();
            using var client = new HttpClient(handler);

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "next_url", "" },
                { "mail", mail },
                { "password", password }
            });

            try
            {
                var res = await client.PostAsync(loginUrl, content).ConfigureAwait(false);
                userid = res.Headers.GetValues("x-niconico-id").FirstOrDefault();
            }
            catch (HttpRequestException e)
            {
                throw new NetworkNiconicoLoginSessionException(e);
            }
            catch(InvalidOperationException e)
            {
                throw new NetworkNiconicoLoginSessionException(e);
            }
            
            CookieCollection cookieCollection = handler.CookieContainer.GetCookies(new Uri(loginUrl));
            if (cookieCollection.All(x => x.Name != "user_session"))
                throw new LoginFailureNiconicoLoginSessionException();

            cookie = cookieCollection;
        }

        /// <summary>
        /// ログアウトする
        /// </summary>
        /// <exception cref="InvalidOperationException">ログインしていない</exception>
        /// <exception cref="NetworkNiconicoLoginSessionException"></exception>
        public async Task Logout()
        {
            if (!IsLoggedin)
                throw new InvalidOperationException("ログインしていません");

            var handler = new HttpClientHandler();
            using var client = new HttpClient(handler);

            handler.CookieContainer.Add(cookie);
            try
            {
                await client.GetAsync("https://secure.nicovideo.jp/secure/logout").ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                throw new NetworkNiconicoLoginSessionException(e);
            }
            cookie = null;
            userid = null;
        }

        public async Task Authorization(string token)
        {
            this.token = token;
            const string openidUserinfoUrl = "https://oauth.nicovideo.jp/open_id/userinfo";

            using var client = new HttpClient();

            var request = new HttpRequestMessage(HttpMethod.Get, openidUserinfoUrl);
            request.Headers.Add("ContentType", "application/json");
            request.Headers.Add("Authorization", $"Bearer {token}");

            try
            {
                var res = await client.SendAsync(request);
                var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
                userid = json.GetProperty("sub").ToString();
            }
            catch (HttpRequestException e)
            {
                throw new NetworkNiconicoLoginSessionException(e);
            }
            catch (InvalidOperationException e)
            {
                throw new NetworkNiconicoLoginSessionException(e);
            }
            catch (KeyNotFoundException e)
            {
                throw new NetworkNiconicoLoginSessionException(e);
            }

        }

        public void Disapproved()
        {
            if (!IsAuthorization)
                throw new InvalidOperationException("認可情報取得していません");
            token = null;
            userid = null;
        }
    }
}
