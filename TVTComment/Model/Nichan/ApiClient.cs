
// 参考: http://prokusi.wiki.fc2.com/wiki/API%E4%BB%95%E6%A7%98

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
        public NetworkApiClientException(Exception innerException) : base(null, innerException)
        {
        }
    }
    /// <summary>
    /// サーバーから返信が返ってきたが内容にエラーがあるときの例外
    /// </summary>
    public class ResponseApiClientException : ApiClientException
    {
    }
    /// <summary>
    /// サーバーでの認証に問題があるときの例外
    /// </summary>
    public class AuthorizationApiClientException : ResponseApiClientException
    {
    }


    public class ApiClient : IDisposable
    {
        private readonly HttpClient httpClient = new (
        //new HttpClientHandler() {
        //    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        //}
        );

        public async Task<HttpResponseMessage> GetDatResponse(string provider,string server, string board, string threadId, IEnumerable<(string name, string value)> additionalHeaders)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://{server}.{provider}/{board}/dat/{threadId}.dat");

            async Task<HttpResponseMessage> get()
            {
                HttpResponseMessage response;
                try
                {
                    response = await httpClient.SendAsync(request);
                }
                catch (HttpRequestException e)
                {
                    throw new NetworkApiClientException(e);
                }
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new AuthorizationApiClientException();
                }
                return response;
            }

            HttpResponseMessage response;
            try
            {
                response = await get();
            }
            catch (AuthorizationApiClientException)
            {
                response = await get();
            }

            return response;
        }

        public void Dispose()
        {
            httpClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
