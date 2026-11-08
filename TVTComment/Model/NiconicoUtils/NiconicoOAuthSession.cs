using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model.NiconicoUtils
{
    sealed class NiconicoOAuthSession : NiconicoLoginSession
    {
        private static readonly HttpClient defaultHttpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        }) { Timeout = TimeSpan.FromSeconds(30) };
        private readonly HttpClient httpClient;
        private readonly NiconicoOAuthClient client;
        private readonly SemaphoreSlim refreshLock = new SemaphoreSlim(1, 1);
        private volatile bool invalidated;
        private bool needsSave;

        internal NiconicoOAuthCredentials Credentials { get; private set; }
        internal Func<Task<bool>> ChangedAsync { get; set; } = () => Task.FromResult(true);
        internal bool IsExpired => invalidated || Credentials.SessionExpiresAt <= DateTimeOffset.UtcNow;

        internal NiconicoOAuthSession(NiconicoOAuthCredentials credentials, NiconicoOAuthClient client = null, HttpClient httpClient = null)
            : base(new CookieCollection(), credentials?.UserId)
        {
            if (credentials == null || !NiconicoOAuthClient.IsSealedToken(credentials.SessionToken) ||
                !Regex.IsMatch(credentials.UserId ?? "", "\\A[0-9]{1,32}\\z") || string.IsNullOrEmpty(credentials.AccessToken) ||
                credentials.ExpiresAt == default || credentials.SessionExpiresAt == default)
                throw new FormatException("OAuthのセッション保存データが不正です。");
            NiconicoOAuthClient.ValidateOrigin(credentials.ServerOrigin);
            Credentials = credentials;
            this.client = client ?? new NiconicoOAuthClient();
            this.httpClient = httpClient ?? defaultHttpClient;
        }

        internal void Invalidate() => invalidated = true;

        internal async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, string rejectedToken = null)
        {
            await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsExpired)
                {
                    await ChangedAsync().ConfigureAwait(false);
                    throw new NiconicoSessionExpiredException();
                }
                if (Credentials.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(60) || Credentials.AccessToken == rejectedToken)
                {
                    NiconicoOAuthCredentials updated;
                    try { updated = await client.RefreshAsync(Credentials, cancellationToken).ConfigureAwait(false); }
                    catch (NiconicoOAuthException e) when (e.RequiresAuthorization)
                    {
                        Invalidate();
                        await ChangedAsync().ConfigureAwait(false);
                        throw new NiconicoSessionExpiredException();
                    }
                    if (invalidated) throw new NiconicoSessionExpiredException();
                    Credentials = updated;
                    needsSave = true;
                }
                if (needsSave) needsSave = !await ChangedAsync().ConfigureAwait(false);
                if (IsExpired) throw new NiconicoSessionExpiredException();
                return Credentials.AccessToken;
            }
            finally { refreshLock.Release(); }
        }

        private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
        {
            string rejectedToken = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var token = await GetAccessTokenAsync(cancellationToken, rejectedToken).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.live2.nicovideo.jp/api/v1/" + path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.TryAddWithoutValidation("User-Agent", Utils.MiscUtils.GetUserAgent());
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    rejectedToken = token;
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            Invalidate();
            await ChangedAsync().ConfigureAwait(false);
            throw new NiconicoSessionExpiredException();
        }

        internal async Task<(string webSocketUrl, bool notOnAir, long openTime)> GetWatchDataAsync(string liveId, CancellationToken cancellationToken)
        {
            if (Regex.IsMatch(liveId ?? "", "\\Ach[0-9]+\\z"))
            {
                try
                {
                    using var channel = await GetJsonAsync("watch/channels/programs/onair?id=" + liveId, cancellationToken).ConfigureAwait(false);
                    liveId = channel.RootElement.GetProperty("data").GetProperty("programId").GetString();
                }
                catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new LiveNotFoundException("放送が終了しています。");
                }
            }
            if (!Regex.IsMatch(liveId ?? "", "\\Alv[0-9]+\\z"))
                throw new NicoLiveCommentSenderException("生放送IDまたはチャンネルIDを確認してください。");

            var query = $"nicoliveProgramId={liveId}&userId={UserId}";
            using var program = await GetJsonAsync("watch/programs?" + query + "&fields=program", cancellationToken).ConfigureAwait(false);
            var schedule = program.RootElement.GetProperty("data").GetProperty("program").GetProperty("schedule");
            if (schedule.GetProperty("status").GetString() != "ON_AIR") return (null, true, 0);
            var openTime = schedule.GetProperty("vposBaseTime").GetDateTimeOffset().ToUnixTimeSeconds();
            using var endpoint = await GetJsonAsync("wsendpoint?" + query, cancellationToken).ConfigureAwait(false);
            var url = endpoint.RootElement.GetProperty("data").GetProperty("url").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "wss" || uri.UserInfo.Length != 0)
                throw new NicoLiveCommentSenderException("視聴セッションの接続先が不正です。");
            return (url, false, openTime);
        }
    }
}
