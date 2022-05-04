using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace TVTComment.Model.NiconicoUtils
{
    class NicoLiveCommentReceiverException : Exception
    {
        public NicoLiveCommentReceiverException() { }
        public NicoLiveCommentReceiverException(string message) : base(message) { }
        public NicoLiveCommentReceiverException(string message, Exception inner) : base(message, inner) { }
    }

    class InvalidPlayerStatusNicoLiveCommentReceiverException : NicoLiveCommentReceiverException
    {
        public string PlayerStatus { get; }
        public InvalidPlayerStatusNicoLiveCommentReceiverException(string playerStatus)
        {
            PlayerStatus = playerStatus;
        }
    }

    class NetworkNicoLiveCommentReceiverException : NicoLiveCommentReceiverException
    {
        public NetworkNicoLiveCommentReceiverException(Exception inner) : base(null, inner)
        {
        }
    }
    class ConnectionClosedNicoLiveCommentReceiverException : NicoLiveCommentReceiverException
    {
        public ConnectionClosedNicoLiveCommentReceiverException()
        {
        }
    }
    class ConnectionDisconnectNicoLiveCommentReceiverException : NicoLiveCommentReceiverException
    {
        public ConnectionDisconnectNicoLiveCommentReceiverException()
        {
        }
    }

    class NicoLiveCommentReceiver : IDisposable
    {
        public NiconicoLoginSession NiconicoLoginSession { get; }
        private int count;
        private readonly string ua;

        public NicoLiveCommentReceiver(NiconicoLoginSession niconicoLoginSession)
        {
            NiconicoLoginSession = niconicoLoginSession;

            var handler = new HttpClientHandler();
            handler.CookieContainer.Add(niconicoLoginSession.Cookie);
            httpClient = new HttpClient(handler);
            var assembly = Assembly.GetExecutingAssembly().GetName();
            ua = assembly.Name + "/" + assembly.Version.ToString(3);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua);
            count = Environment.TickCount;
        }

        /// <summary>
        /// 受信した<see cref="NiconicoCommentXmlTag"/>を無限非同期イテレータで返す
        /// </summary>
        /// <exception cref="OperationCanceledException"></exception>
        /// <exception cref="InvalidPlayerStatusNicoLiveCommentReceiverException"></exception>
        /// <exception cref="NetworkNicoLiveCommentReceiverException"></exception>
        /// <exception cref="ConnectionClosedNicoLiveCommentReceiverException"></exception>
        public async IAsyncEnumerable<NiconicoCommentXmlTag> Receive(string liveId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var timer = new System.Timers.Timer(60000);
            using var _ = cancellationToken.Register(() =>
           {
               httpClient.CancelPendingRequests();
               timer.Dispose();
           });
            var comId = "";

            for (int disconnectedCount = 0; disconnectedCount < 5; ++disconnectedCount)
            {
                var random = new Random();
                await Task.Delay((disconnectedCount * 5000) + random.Next(0, 101)); //再試行時に立て続けのリクエストにならないようにする
                Stream str;
                try
                {
                    if (comId != "") { //コミュIDが取得済みであればlvを再取得　24時間放送のコミュニティ・チャンネル用
                        var getLiveId = await httpClient.GetStreamAsync($"https://live2.nicovideo.jp/unama/tool/v1/broadcasters/social_group/{comId}/program", cancellationToken).ConfigureAwait(false);
                        var liveIdJson = await JsonDocument.ParseAsync(getLiveId, cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (!liveIdJson.RootElement.GetProperty("meta").GetProperty("errorCode").GetString().Equals("OK")) throw new InvalidPlayerStatusNicoLiveCommentReceiverException("コミュニティ・チャンネルが見つかりませんでした");
                        liveId = liveIdJson.RootElement.GetProperty("data").GetProperty("nicoliveProgramId").GetString();
                    }
                    str = await httpClient.GetStreamAsync($"https://live2.nicovideo.jp/unama/watch/{liveId}/programinfo", cancellationToken).ConfigureAwait(false);
                }
                // httpClient.CancelPendingRequestsが呼ばれた、もしくはタイムアウト
                catch (TaskCanceledException e)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(null, e, cancellationToken);
                    throw new NetworkNicoLiveCommentReceiverException(e);
                }
                catch (HttpRequestException e)
                {
                    throw new NetworkNicoLiveCommentReceiverException(e);
                }
                var playerStatus = await JsonDocument.ParseAsync(str, cancellationToken: cancellationToken).ConfigureAwait(false);
                var playerStatusRoot = playerStatus.RootElement;
                comId = playerStatusRoot.GetProperty("data").GetProperty("socialGroup").GetProperty("id").GetString(); //コメント受信ループ内でbreakされたあとにコミュからlvを取得するためにコミュを取得しておく
                if (playerStatusRoot.GetProperty("data").GetProperty("rooms").GetArrayLength() <= 0) //roomsが無かったら放送終了扱い
                    throw new ConnectionDisconnectNicoLiveCommentReceiverException();
                var msUriStr = playerStatusRoot.GetProperty("data").GetProperty("rooms")[0].GetProperty("webSocketUri").GetString();
                var threadId = playerStatusRoot.GetProperty("data").GetProperty("rooms")[0].GetProperty("threadId").GetString();
                if (threadId == null || msUriStr == null)
                {
                    throw new InvalidPlayerStatusNicoLiveCommentReceiverException(str.ToString());
                }
                var msUri = new Uri(msUriStr);

                using var ws = new ClientWebSocket();

                ws.Options.SetRequestHeader("User-Agent", ua);
                ws.Options.AddSubProtocol("msg.nicovideo.jp#json");

                try
                {
                    await ws.ConnectAsync(msUri, cancellationToken);
                }
                catch (Exception e) when (e is ObjectDisposedException || e is WebSocketException || e is IOException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(null, e, cancellationToken);
                    if (e is ObjectDisposedException)
                        throw;
                    else
                        throw new NetworkNicoLiveCommentReceiverException(e);
                }

                using var __ = cancellationToken.Register(() => {
                    ws.Dispose();
                });

                var sendThread = "[{\"ping\":{\"content\":\"rs:0\"}},{\"ping\":{\"content\":\"ps:0\"}},{\"thread\":{\"thread\":\""+ threadId + "\",\"version\":\"20061206\",\"user_id\":\""+ NiconicoLoginSession.UserId + "\",\"res_from\":-10,\"with_global\":1,\"scores\":1,\"nicoru\":0}},{\"ping\":{\"content\":\"pf:0\"}},{\"ping\":{\"content\":\"rf:0\"}}]";
                
                try
                {
                    var bodyEncoded = Encoding.UTF8.GetBytes(sendThread);
                    var segment = new ArraySegment<byte>(bodyEncoded);
                    await ws.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
                }
                catch (Exception e) when (e is ObjectDisposedException || e is WebSocketException || e is IOException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(null, e, cancellationToken);
                    if (e is ObjectDisposedException)
                        throw;
                    else
                        throw new NetworkNicoLiveCommentReceiverException(e);
                }

                ElapsedEventHandler handler = null;
                handler = async (sender, e) => //60秒ごと定期的に空リクエスト送信　コメント無いときの切断防止
                {
                    if (ws.State != WebSocketState.Open)
                    {
                        timer.Elapsed -= handler;
                        timer.Close();
                        return;
                    }
                    await ws.SendAsync(new ArraySegment<byte>(Array.Empty<byte>()), WebSocketMessageType.Text, true, cancellationToken);
                };
                timer.Elapsed += handler;

                timer.Start();
                var buffer = new byte[4096];
                //コメント受信ループ
                while (true)
                {
                    if (ws.State != WebSocketState.Open) {
                        timer.Elapsed -= handler;
                        timer.Close();
                        break;
                    }

                    var segment = new ArraySegment<byte>(buffer);
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await ws.ReceiveAsync(segment, cancellationToken);
                    }
                    catch (Exception e) when (e is ObjectDisposedException || e is WebSocketException || e is IOException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException(null, e, cancellationToken);
                        if (e is ObjectDisposedException)
                            throw;
                        else
                            throw new NetworkNicoLiveCommentReceiverException(e);
                    }
                    if (result.MessageType == WebSocketMessageType.Close) //切断要求だったらException
                        throw new ConnectionClosedNicoLiveCommentReceiverException();
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try { 
                        parser.Push(message);
                    }
                    catch (ConnectionDisconnectNicoLiveCommentReceiverException) { //Disconnectメッセージだったら
                        timer.Elapsed -= handler;
                        timer.Close();
                        break;
                    }
                    catch (Exception) {
                        throw;
                    }
                    while (parser.DataAvailable())
                        yield return parser.Pop();
                }
                timer.Elapsed -= handler;
                timer.Close();
            }
            throw new ConnectionClosedNicoLiveCommentReceiverException();
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }

        private readonly HttpClient httpClient;
        private readonly NiconicoCommentJsonParser parser = new NiconicoCommentJsonParser();
    }
}
