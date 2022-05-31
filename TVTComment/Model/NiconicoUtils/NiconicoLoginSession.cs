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
        private string token = null;
        private string userid = null;

        public bool IsAuthorization => token != null;

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

        public NiconicoLoginSession()
        {
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
