using System;
using System.Net;

namespace TVTComment.Model.NiconicoUtils
{
    class NiconicoLoginSessionException : Exception
    {
        public NiconicoLoginSessionException(string message, Exception inner = null) : base(message, inner) { }
    }

    class NiconicoSessionExpiredException : NiconicoLoginSessionException
    {
        public NiconicoSessionExpiredException() : base("ログインの有効期限が切れました。設定の「ログイン」から再度ログインしてください。") { }
    }

    class NiconicoLoginSession
    {
        private readonly CookieContainer cookies = new CookieContainer();
        public bool IsLoggedin => true;
        public string UserId { get; }
        public CookieCollection Cookie => cookies.GetAllCookies();

        internal NiconicoLoginSession(CookieCollection cookies, string userId)
        {
            this.cookies.Add(cookies);
            UserId = userId;
        }

        internal static bool IsServiceDomain(string domain)
        {
            domain = domain.TrimStart('.');
            return domain.Equals("nicovideo.jp", StringComparison.OrdinalIgnoreCase)
                || domain.EndsWith(".nicovideo.jp", StringComparison.OrdinalIgnoreCase);
        }
    }
}
