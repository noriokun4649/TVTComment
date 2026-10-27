using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model.NiconicoUtils
{
    enum NiconicoAuthenticationState { SignedOut, Authenticating, SignedIn, Restoring, Expired }

    sealed class NiconicoSessionClient : IDisposable
    {
        private readonly CookieContainer cookies = new CookieContainer(1000, 100, 16384);
        private readonly HttpClient client;

        public NiconicoSessionClient(CookieCollection cookies = null, HttpMessageHandler handler = null)
        {
            if (cookies != null) this.cookies.Add(cookies);
            client = new HttpClient(handler ?? new HttpClientHandler { CookieContainer = this.cookies, AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Utils.MiscUtils.GetUserAgent());
        }

        public async Task<NiconicoLoginSession> ValidateAsync(CancellationToken cancellationToken)
        {
            var uri = new Uri("https://live.nicovideo.jp/");
            for (int i = 0; i < 10; i++)
            {
                if (uri.Scheme != "https" || !NiconicoLoginSession.IsServiceDomain(uri.Host))
                    throw new NiconicoLoginSessionException("セッション確認の転送先が不正です。");
                using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                {
                    if (response.Headers.Location == null) throw new NiconicoLoginSessionException("セッション確認の転送先がありません。");
                    uri = new Uri(uri, response.Headers.Location);
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    throw new NiconicoSessionExpiredException();
                response.EnsureSuccessStatusCode();
                using var document = ParseEmbeddedData(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                var data = document.RootElement.TryGetProperty("props", out var props) ? props : document.RootElement;
                if (!data.TryGetProperty("user", out var user) || !user.TryGetProperty("isLoggedIn", out var loggedIn) || loggedIn.ValueKind != JsonValueKind.True)
                    throw new NiconicoSessionExpiredException();
                var id = user.TryGetProperty("id", out var value) ? value.ToString()
                    : user.TryGetProperty("userId", out value) ? value.ToString() : "";
                if (!long.TryParse(id, out long number) || number <= 0) throw new NiconicoLoginSessionException("ログインユーザーの情報を確認できませんでした。");
                return new NiconicoLoginSession(cookies.GetAllCookies(), id);
            }
            throw new NiconicoLoginSessionException("セッション確認の転送回数を超えました。");
        }

        internal static JsonDocument ParseEmbeddedData(string html)
        {
            var match = Regex.Match(html, @"<script\b(?=[^>]*\bid=['""]embedded-data['""])[^>]*\bdata-props=['""]([^'""]+)['""][^>]*>", RegexOptions.IgnoreCase);
            if (!match.Success)
                match = Regex.Match(html, @"<script\b(?=[^>]*\bid=['""]DAT-csr-data['""])[^>]*\bdata-value=['""]([^'""]+)['""][^>]*>", RegexOptions.IgnoreCase);
            if (!match.Success) throw new NiconicoLoginSessionException("生放送のユーザー情報を解析できませんでした。");
            return JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
        }

        public void Dispose() => client.Dispose();
    }
}
