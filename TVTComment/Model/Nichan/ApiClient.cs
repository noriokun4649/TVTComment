using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Nichan
{
    public class ApiClientException : NichanException
    {
        public ApiClientException() { }
        public ApiClientException(string message) : base(message) { }
        public ApiClientException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// サーバーから返信がない（サーバーに接続できない）ときの例外
    /// </summary>
    public class NetworkApiClientException : ApiClientException
    {
        public NetworkApiClientException(Exception innerException) : base("5chとの通信に失敗しました。", innerException)
        {
        }
    }
    /// <summary>
    /// サーバーから返信が返ってきたが内容にエラーがあるときの例外
    /// </summary>
    public class ResponseApiClientException : ApiClientException
    {
        public HttpStatusCode StatusCode { get; }
        public TimeSpan? RetryAfter { get; }
        public ResponseApiClientException(HttpStatusCode statusCode = 0, TimeSpan? retryAfter = null)
            : base($"DAT取得に失敗しました（HTTP {(int)statusCode}）。")
        {
            StatusCode = statusCode;
            RetryAfter = retryAfter;
        }
    }
    /// <summary>
    /// サーバーでの認証に問題があるときの例外
    /// </summary>
    public class AuthorizationApiClientException : ResponseApiClientException
    {
        public AuthorizationApiClientException(HttpStatusCode statusCode = HttpStatusCode.Forbidden) : base(statusCode) { }
    }
    public sealed class ThreadUnavailableException : ResponseApiClientException
    {
        public ThreadUnavailableException(HttpStatusCode statusCode) : base(statusCode) { }
    }
    public class ApiClient : IDisposable
    {
        private readonly HttpClient httpClient;

        public ApiClient(HttpMessageHandler handler = null)
        {
            httpClient = BoardClient.CreateHttpClient(handler);
        }

        public Task<HttpResponseMessage> GetDatResponse(
            string provider, string server, string board, string threadId,
            IEnumerable<(string name, string value)> additionalHeaders, CancellationToken cancellationToken = default
        )
            => GetDatResponse(new ThreadAddress(new Uri($"https://{server}.{provider}/{board}/"), threadId), additionalHeaders, cancellationToken);

        public async Task<HttpResponseMessage> GetDatResponse(ThreadAddress address, IEnumerable<(string name, string value)> additionalHeaders, CancellationToken token = default)
        {
            var board = BoardClient.Shared.GetKnownBoard(address.BoardUri);
            var uri = new ThreadAddress(board, address.ThreadId).DatUri;
            for (int attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
                if (additionalHeaders != null)
                    foreach (var (name, value) in additionalHeaders)
                        request.Headers.TryAddWithoutValidation(name, value);
                HttpResponseMessage response;
                try
                {
                    response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    if (attempt >= 2)
                        throw new NetworkApiClientException(e);
                    await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token).ConfigureAwait(false);
                    continue;
                }
                if (attempt < 2 && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
                {
                    var delay = GetRetryAfter(response) ?? TimeSpan.FromSeconds(attempt + 1);
                    // 長いRetry-Afterは収集ループへ返し、他スレッドを止めない
                    if (delay > TimeSpan.FromSeconds(10))
                        return response;
                    response.Dispose();
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    continue;
                }
                return response;
            }
        }
        internal static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow);
            return delay.HasValue && delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
        public void Dispose()
        {
            httpClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
