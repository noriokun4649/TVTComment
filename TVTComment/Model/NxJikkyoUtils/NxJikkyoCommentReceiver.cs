using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using TVTComment.Model.NiconicoUtils;
using static TVTComment.Model.ChatCollectService.NxJikkyoChatCollectService;

namespace TVTComment.Model.NxJikkyoUtils
{
    class NxJikkyoCommentReceiver : IDisposable
    {
        private readonly string ua;

        public NxJikkyoCommentReceiver()
        {
            parser = new NiconicoCommentJsonParser();
            var assembly = Assembly.GetExecutingAssembly().GetName();
            ua = assembly.Name + "/" + assembly.Version.ToString(3);
        }

        /// <summary>
        /// 受信した<see cref="NiconicoCommentXmlTag"/>を無限非同期イテレータで返す
        /// </summary>
        /// <exception cref="OperationCanceledException"></exception>
        /// <exception cref="InvalidPlayerStatusNicoLiveCommentReceiverException"></exception>
        /// <exception cref="NetworkNicoLiveCommentReceiverException"></exception>
        /// <exception cref="ConnectionClosedNicoLiveCommentReceiverException"></exception>
        public async IAsyncEnumerable<NiconicoCommentXmlTag> Receive(int jkid, RoomObject room, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var timer = new System.Timers.Timer(60000);
            using var _ = cancellationToken.Register(() =>
            {
                timer.Dispose();
           });

            for (int disconnectedCount = 0; disconnectedCount < 5; ++disconnectedCount)
            {
                var random = new Random();
                await Task.Delay((disconnectedCount * 5000) + random.Next(0, 101), cancellationToken).ConfigureAwait(false); //再試行時に立て続けのリクエストにならないようにする

                var msUri = new Uri("wss://nx-jikkyo.tsukumijima.net/api/v1/channels/jk"+ jkid +"/ws/comment");

                using var ws = new ClientWebSocket();

                ws.Options.SetRequestHeader("User-Agent", ua);
                ws.Options.AddSubProtocol("msg.nicovideo.jp#json");
                ws.Options.KeepAliveInterval = TimeSpan.Zero;

                try
                {
                    await ws.ConnectAsync(msUri, cancellationToken);
                }
                catch (Exception e) when (e is ObjectDisposedException || e is WebSocketException || e is IOException )
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

                var sendThread = "[{\"ping\":{\"content\":\"rs:0\"}},{\"ping\":{\"content\":\"ps:0\"}},{\"thread\":{\"thread\":\""+ room.threadId + "\",\"version\":\"20061206\",\"user_id\":\"\",\"res_from\":-10,\"with_global\":1,\"scores\":1,\"nicoru\":0 ,\"threadkey\":\""+ room.yourPostKey +"\"}},{\"ping\":{\"content\":\"pf:0\"}},{\"ping\":{\"content\":\"rf:0\"}}]";
                
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

        }

        private readonly NiconicoCommentJsonParser parser;
    }
}
