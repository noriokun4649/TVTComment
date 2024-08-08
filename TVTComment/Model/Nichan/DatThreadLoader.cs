// 参考: http://info.5ch.net/index.php/Monazilla/develop/access

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Nichan
{
    public class DatThreadLoaderException : NichanException
    {
        public DatThreadLoaderException() { }
        public DatThreadLoaderException(string message) : base(message) { }
        public DatThreadLoaderException(string message, Exception inner) : base(message, inner) { }
    }

    public class DatFormatDatThreadLoaderException : DatThreadLoaderException
    {
        public string DatString { get; }

        public DatFormatDatThreadLoaderException(string datString)
        {
            DatString = datString;
        }

        public DatFormatDatThreadLoaderException(string datString, System.Exception inner) : base(null, inner)
        {
            DatString = datString;
        }
    }

    /// <summary>
    /// datを差分取得しながらスレを更新する
    /// </summary>
    public class DatThreadLoader
    {
        public string ServiceProvider { get; }
        public string ServerName { get; }
        public string BoardName { get; }
        public string ThreadId { get; }

        /// <summary>
        /// 取得結果の<see cref="Nichan.Thread"/>。<br/>
        /// <see cref="Update(ApiClient)"/>を呼ぶ度に変更があれば更新されていく。
        /// ただし<see cref="Thread.Uri"/>はnull。
        /// </summary>
        public Thread Thread { get; } = new Thread();
        /// <summary>
        /// 前回の<see cref="Update(ApiClient)"/>の呼び出しで更新があったか
        /// </summary>
        public bool LastUpdated { get; private set; } = false;
        /// <summary>
        /// 前回の<see cref="Update(ApiClient)"/>の呼び出しで全体の更新があったか<br/>
        /// レス削除等で差分更新ができなかった場合に真になる
        /// </summary>
        public bool LastAllUpdated { get; private set; } = false;

        public DatThreadLoader(string serviceProvider, string serverName, string boardName, string threadId)
        {
            (ServiceProvider, ServerName, BoardName, ThreadId) = (serviceProvider, serverName, boardName, threadId);
            Thread.Name = threadId;
        }

        /// <summary>
        /// <paramref name="apiClient"/>を使って<see cref="Thread"/>を更新する<br/>
        /// <see cref="ApiClient"/>および<see cref="DatParser"/>が投げる例外を投げる可能性がある
        /// </summary>
        public async Task Update(ApiClient apiClient)
        {
            LastUpdated = LastAllUpdated = false;

            var headers = new List<(string, string)>();
            if (loadedNumBytes > 0)
            {
                headers.Add(("Range", $"bytes={loadedNumBytes - 1}-"));
            }
            if (lastModified != "")
            {
                headers.Add(("If-Modified-Since", lastModified));
            }

            HttpResponseMessage response = await apiClient.GetDatResponse(
                ServiceProvider, ServerName, BoardName, ThreadId, headers
            );

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // 変更なし
                return;
            }

            if (response.Content.Headers.TryGetValues("Last-Modified", out var values))
            {
                lastModified = values.First();
            }

            byte[] responseBodyBytes = await response.Content.ReadAsByteArrayAsync();
            string responseBody = Encoding.GetEncoding(932).GetString(responseBodyBytes);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                // 全体更新
                loadedNumBytes = responseBodyBytes.Length;
                datParser.Reset();
                try
                {
                    datParser.Feed(responseBody);
                }
                catch (DatParserException e)
                {
                    throw new DatFormatDatThreadLoaderException(responseBody, e);
                }
                Thread.Title = datParser.ThreadTitle;
                Thread.Res.Clear();
                LastUpdated = LastAllUpdated = true;
            }
            else if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var contentRange = response.Content.Headers.GetValues("Content-Range").First();
                if (responseBody.Length > 0 && responseBody[0] == '\n')
                {
                    // 差分更新
                    (int startIdx, int endIdx) = (contentRange.IndexOf('-'), contentRange.IndexOf('/'));
                    loadedNumBytes = int.Parse(contentRange[(startIdx + 1)..endIdx]) + 1;
                    try
                    {
                        datParser.Feed(responseBody[1..]);
                    }
                    catch (DatParserException e)
                    {
                        throw new DatFormatDatThreadLoaderException(responseBody[1..], e);
                    }
                }
                else
                {
                    // 過去のレスが削除などで変更されてる→全体再取得
                    (loadedNumBytes, lastModified) = (0, "");
                    await Update(apiClient);
                    return;
                }
            }
            else if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // 過去のレスが削除などで変更されてる→全体再取得
                (loadedNumBytes, lastModified) = (0, "");
                await Update(apiClient);
                return;
            }
            else if (
                response.StatusCode == HttpStatusCode.NotImplemented &&
                headers.Any(x => x.Item1 == "Range") &&
                !response.Headers.Contains("Accept-Ranges")
            )
            {
                // スレが落ちしてるときにRange要求するとここに来る→全体再取得
                (loadedNumBytes, lastModified) = (0, "");
                await Update(apiClient);
                return;
            }
            else
            {
                throw new ResponseApiClientException();
            }

            while (true)
            {
                Res res = datParser.PopRes();
                if (res == null) break;
                Thread.Res.Add(res);
                LastUpdated = true;
            }
            Thread.ResCount = Thread.Res.Count;
        }

        private string lastModified = "";
        private int loadedNumBytes = 0;
        private readonly DatParser datParser = new DatParser();
    }
}
