using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model.NiconicoUtils
{
    class NiconicoOAuthCredentials
    {
        public string ServerOrigin { get; set; }
        public string SessionToken { get; set; }
        public string AccessToken { get; set; }
        public string UserId { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset SessionExpiresAt { get; set; }
    }

    class NiconicoOAuthException : NiconicoLoginSessionException
    {
        public bool RequiresAuthorization { get; }
        public NiconicoOAuthException(string message, bool requiresAuthorization = false) : base(message)
        {
            RequiresAuthorization = requiresAuthorization;
        }
    }

    class NiconicoOAuthClient
    {
        internal const string ServerOrigin = "https://oauth.nk4.jp";
        private static readonly Uri serverOrigin = new Uri(ServerOrigin);
        private static readonly HttpClient defaultHttpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        }) { Timeout = TimeSpan.FromSeconds(30) };
        private readonly HttpClient httpClient;
        private readonly Action<Uri> openBrowser;

        public NiconicoOAuthClient(HttpClient httpClient = null, Action<Uri> openBrowser = null)
        {
            this.httpClient = httpClient ?? defaultHttpClient;
            this.openBrowser = openBrowser ?? (uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }));
        }

        public static Uri ValidateOrigin(string value)
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !uri.IsDefaultPort || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                uri.UserInfo.Length != 0 || uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback ||
                !string.Equals(uri.GetLeftPart(UriPartial.Authority), ServerOrigin, StringComparison.Ordinal))
                throw new NiconicoOAuthException("保存したOAuth認証情報の接続先が現在のサーバと異なります。設定の「ログイン」から再度ログインしてください。", true);
            return serverOrigin;
        }

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        internal static bool IsSealedToken(string value) => value != null && value.Length <= 6144 &&
            Regex.IsMatch(value, "\\Av1\\.[A-Za-z0-9_-]{16}\\.[A-Za-z0-9_-]{22,6100}\\z");
        private static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));

        public async Task<NiconicoOAuthCredentials> AuthorizeAsync(CancellationToken cancellationToken)
        {
            var verifier = RandomValue();
            var state = RandomValue();
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                listener.Server.ExclusiveAddressUse = true;
                listener.Start(4);
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var url = new Uri(serverOrigin, $"/oauth/start?port={port}&state={state}&code_challenge={challenge}&code_challenge_method=S256");
                openBrowser(url);
                var code = await ReceiveCodeAsync(listener, port, state, timeout.Token).ConfigureAwait(false);
                var credentials = new NiconicoOAuthCredentials { ServerOrigin = NiconicoOAuthClient.ServerOrigin };
                return await RequestTokensAsync(credentials, "/oauth2/token", new { code, code_verifier = verifier }, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new NiconicoOAuthException("認証の待ち時間を過ぎました。認証をやり直してください。");
            }
            catch (Exception e) when (e is SocketException || e is Win32Exception)
            {
                throw new NiconicoOAuthException("ブラウザ認証を開始できませんでした。既定のブラウザとローカル接続の設定を確認してください。");
            }
            finally { listener.Stop(); }
        }

        private static async Task<string> ReceiveCodeAsync(TcpListener listener, int port, string state, CancellationToken cancellationToken)
        {
            while (true)
            {
                using var connection = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                using var stream = connection.GetStream();
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var buffer = new byte[8192];
                    int length = 0;
                    string headers = "";
                    while (length < buffer.Length && !headers.Contains("\r\n\r\n"))
                    {
                        int read = await stream.ReadAsync(buffer.AsMemory(length), readTimeout.Token).ConfigureAwait(false);
                        if (read == 0) break;
                        length += read;
                        headers = Encoding.ASCII.GetString(buffer, 0, length);
                    }
                    bool valid = TryReadCallback(headers, port, state, out var code, out var error);
                    // 認証結果の受信後に表示する、OAuthサーバの共通画面
                    var resultUrl = new Uri(serverOrigin, error == null ? "/oauth/complete" : "/oauth/cancelled");
                    var content = Encoding.UTF8.GetBytes(valid
                        ? $"<!doctype html><html lang=\"ja\"><meta charset=\"utf-8\"><title>TVTComment</title><a href=\"{resultUrl.AbsoluteUri}\">TVTCommentの認証画面へ戻る</a></html>"
                        : "Invalid OAuth callback.");
                    var location = valid ? $"Location: {resultUrl.AbsoluteUri}\r\n" : "";
                    var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid ? "303 See Other" : "400 Bad Request")}\r\n{location}Content-Type: text/html; charset=utf-8\r\nContent-Length: {content.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; frame-ancestors 'none'\r\nX-Content-Type-Options: nosniff\r\n\r\n");
                    await stream.WriteAsync(head.AsMemory(), readTimeout.Token).ConfigureAwait(false);
                    await stream.WriteAsync(content.AsMemory(), readTimeout.Token).ConfigureAwait(false);
                    if (!valid) continue;
                    if (error != null) throw new NiconicoOAuthException("認証が拒否されたか、サーバの利用制限に達しました。時間をおいて認証をやり直してください。");
                    return code;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                catch (IOException) when (!cancellationToken.IsCancellationRequested) { }
            }
        }

        internal static bool TryReadCallback(string headers, int port, string expectedState, out string code, out string error)
        {
            code = error = null;
            if (!headers.EndsWith("\r\n\r\n", StringComparison.Ordinal)) return false;
            var lines = headers.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length != 3 || first[0] != "GET" || first[2] != "HTTP/1.1" || !first[1].StartsWith("/callback/?", StringComparison.Ordinal)) return false;
            int hosts = 0;
            foreach (var line in lines)
            {
                if (!line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(line.Substring(5).Trim(), $"127.0.0.1:{port}", StringComparison.Ordinal)) return false;
                hosts++;
            }
            if (hosts != 1) return false;
            var values = new Dictionary<string, string>();
            foreach (var item in first[1].Substring("/callback/?".Length).Split('&'))
            {
                var pair = item.Split('=', 2);
                if (pair.Length != 2) return false;
                string name, value;
                try { name = Uri.UnescapeDataString(pair[0]); value = Uri.UnescapeDataString(pair[1]); }
                catch (UriFormatException) { return false; }
                if (!values.TryAdd(name, value)) return false;
            }
            if (!values.TryGetValue("state", out var state) || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState))) return false;
            values.TryGetValue("code", out code);
            values.TryGetValue("error", out error);
            return (IsSealedToken(code) && error == null) ||
                (code == null && error != null && Regex.IsMatch(error, "\\A[a-z_]{1,64}\\z"));
        }

        public Task<NiconicoOAuthCredentials> RefreshAsync(NiconicoOAuthCredentials credentials, CancellationToken cancellationToken)
            => RequestTokensAsync(credentials, "/oauth/refresh", new { }, cancellationToken);

        private async Task<NiconicoOAuthCredentials> RequestTokensAsync(NiconicoOAuthCredentials credentials, string path, object body, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ValidateOrigin(credentials.ServerOrigin), path));
            if (credentials.SessionToken != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.SessionToken);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var result = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.StatusCode == HttpStatusCode.Unauthorized)
                throw new NiconicoOAuthException("認証の有効期限が切れました。ニコニコ認証をやり直してください。", true);
            if ((int)result.StatusCode == 429)
                throw new NiconicoOAuthException("OAuthサーバの利用制限に達しました。時間をおいて再試行してください。");
            if (!result.IsSuccessStatusCode)
                throw new NiconicoOAuthException("OAuthサーバとの通信に失敗しました。時間をおいて再試行してください。");
            try
            {
                using var document = JsonDocument.Parse(await result.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                var root = document.RootElement;
                var sessionToken = root.GetProperty("session_token").GetString();
                var userId = root.GetProperty("user_id").GetString();
                var accessToken = root.GetProperty("access_token").GetString();
                var expiresIn = root.GetProperty("expires_in").GetInt32();
                var sessionExpiresAt = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("session_expires_at").GetInt64());
                if (!IsSealedToken(sessionToken) ||
                    !Regex.IsMatch(userId ?? "", "\\A[0-9]{1,32}\\z") || string.IsNullOrEmpty(accessToken) ||
                    expiresIn <= 0 || expiresIn > 31536000 || sessionExpiresAt <= DateTimeOffset.UtcNow ||
                    root.GetProperty("token_type").GetString() != "Bearer" ||
                    (credentials.UserId != null && credentials.UserId != userId)) throw new FormatException();
                return new NiconicoOAuthCredentials
                {
                    ServerOrigin = NiconicoOAuthClient.ServerOrigin, SessionToken = sessionToken, AccessToken = accessToken,
                    UserId = userId, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                    SessionExpiresAt = sessionExpiresAt
                };
            }
            catch (Exception e) when (e is JsonException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException || e is ArgumentOutOfRangeException)
            { throw new NiconicoOAuthException("OAuthサーバから不正な応答が返されました。"); }
        }
    }
}
