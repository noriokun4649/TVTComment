using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using TVTComment.Model.NiconicoUtils;
using static TVTComment.Model.ChatCollectService.NxJikkyoChatCollectService;

namespace TVTComment.Model.NxJikkyoUtils
{
    class NxJikkyoCommentSender : IDisposable
    {
        private BlockingCollection<string> errorMesColl = new();
        private readonly string ua;
        private long openTime;
        private ClientWebSocket clientWebSocket;

        public NxJikkyoCommentSender()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            ua = assembly.Name + "/" + assembly.Version.ToString(3);
        }


        public async Task ConnectWatchSession(int jkid, BlockingCollection<RoomObject> postKey, CancellationToken cancellationToken)
        {
            var webScoketUri = new Uri("wss://nx-jikkyo.tsukumijima.net/api/v1/channels/jk"+jkid+"/ws/watch");

            clientWebSocket = new ClientWebSocket();
            clientWebSocket.Options.SetRequestHeader("User-Agent", ua);
            clientWebSocket.Options.KeepAliveInterval = TimeSpan.Zero;

            await clientWebSocket.ConnectAsync(webScoketUri, cancellationToken);
            await WsSend("{\"type\": \"startWatching\",\"data\": {\"reconnect\": false}}", cancellationToken);

            using var timer = new System.Timers.Timer(30 * 1000);

            ElapsedEventHandler handler = null;
            handler = async (sender, e) =>
            {
                if (clientWebSocket.State != WebSocketState.Open)
                {
                    timer.Elapsed -= handler;
                    timer.Close();
                    return;
                }
                await WsSend("{\"type\": \"keepSeat\"}", cancellationToken);
            };
            var buffer = new byte[4096];
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "IsCancellationRequested", CancellationToken.None);
                    timer.Elapsed -= handler;
                    timer.Close();
                    break;
                }
                var segment = new ArraySegment<byte>(buffer);
                WebSocketReceiveResult result;
                try
                {
                    result = await clientWebSocket.ReceiveAsync(segment, cancellationToken);
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
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                var json = JsonDocument.Parse(message).RootElement;
                var type = json.GetProperty("type").GetString();
                switch (type)
                {
                    case "error":
                        var reason = json.GetProperty("data").GetProperty("code").GetString();
                        switch (reason)
                        {
                            case "CONTENT_NOT_READY":
                            case "NO_PERMISSION":
                            case "NOT_ON_AIR":
                            case "BROADCAST_NOT_FOUND":
                                await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "error disconnect", CancellationToken.None);
                                timer.Elapsed -= handler;
                                timer.Close();
                                return;
                        }
                        errorMesColl.Add(reason);
                        break;
                    case "seat":
                        var keepInterval = json.GetProperty("data").GetProperty("keepIntervalSec").GetInt32();
                        timer.Interval = keepInterval * 1000;
                        timer.Elapsed += handler;
                        timer.Start();
                        break;
                    case "ping":
                        await WsSend("{\"type\": \"pong\"}", cancellationToken);
                        break;
                    case "reconnect":
                        var data = json.GetProperty("data");
                        var token = data.GetProperty("audienceToken").GetString();
                        var waittime = data.GetProperty("waitTimeSec").GetInt32();
                        await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", CancellationToken.None);
                        var path = webScoketUri.GetLeftPart(UriPartial.Path);
                        var newUri = new Uri(path + "?audience_token=" + token);
                        await Task.Delay(waittime * 1000);
                        await clientWebSocket.ConnectAsync(newUri, cancellationToken);
                        await WsSend("{\"type\": \"startWatching\",\"data\": {\"reconnect\": false}}", cancellationToken);
                        break;
                    case "disconnect":
                        await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None);
                        timer.Elapsed -= handler;
                        timer.Close();
                        errorMesColl.Add("disconnect");
                        break;
                    case "postCommentResult":
                        var postres = json.GetProperty("data").GetProperty("chat").GetProperty("content").GetString();
                        errorMesColl.Add(postres);
                        break;
                    case "room":
                        var vposBaseTime = json.GetProperty("data").GetProperty("vposBaseTime").GetString();
                        openTime = ((DateTimeOffset)DateTime.Parse(vposBaseTime)).ToUnixTimeSeconds();
                        var yourPostKey = json.GetProperty("data").GetProperty("yourPostKey").GetString();
                        var threadId = json.GetProperty("data").GetProperty("threadId").GetString();
                        postKey.Add(new(yourPostKey,threadId));
                        break;
                }
            }
        }

        private async Task WsSend(string text, CancellationToken cancellationToken)
        {
            var encoded = Encoding.UTF8.GetBytes(text);
            var vs = new ArraySegment<byte>(encoded);
            await clientWebSocket.SendAsync(vs, WebSocketMessageType.Text, true, cancellationToken);
        }

        /// <summary>
        /// コメントを投稿する
        /// </summary>
        public async Task Send(int jkId, string message, string mail)
        {
            if (clientWebSocket.State == WebSocketState.Closed) throw new NicoLiveCommentSenderException("視聴セッションは切断されています");
            var arrayMail = mail.Split(" ");
            var size = arrayMail.FirstOrDefault(x => IsSize(x)) ?? "medium";
            var position = arrayMail.FirstOrDefault(x => IsPosition(x)) ?? "naka";
            var font = arrayMail.FirstOrDefault(x => IsFont(x)) ?? "defont";
            var color = arrayMail.FirstOrDefault(x => IsColor(x) || Regex.IsMatch(x, @"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")) ?? "white";
            var isAnonymous = arrayMail.Contains("184") ? "true" : "false"; //C#はTrue Falseの頭文字が大文字なので
            long vpos = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 10 - openTime * 100; // vposは10ミリ秒単位
            await WsSend("{\"type\":\"postComment\",\"data\":{" +
                "\"text\":\"" + message + "\"," +
                "\"vpos\":" + vpos + "," +
                "\"isAnonymous\":" + isAnonymous + "," +
                "\"color\":\"" + color + "\"," +
                "\"size\":\"" + size + "\"," +
                "\"position\":\"" + position + "\"," +
                "\"font\":\"" + font + "\"" +
                "}}", CancellationToken.None);

            await Task.Delay(1000).ConfigureAwait(false);
            errorMesColl.TryTake(out var text, Timeout.Infinite); //結果を待つ
            switch (text)
            {
                case "disconnect":
                    throw new NicoLiveCommentSenderException("視聴セッションの切断要求がサーバから通知されたため切断します");
                case "CONTENT_NOT_READY":
                    throw new NicoLiveCommentSenderException("配信できない状態である（配信用ストリームエラー）");
                case "NO_PERMISSION":
                    throw new NicoLiveCommentSenderException("APIにアクセスする権限がない");
                case "NOT_ON_AIR":
                    throw new NicoLiveCommentSenderException("放送中ではない");
                case "BROADCAST_NOT_FOUND":
                    throw new NicoLiveCommentSenderException("配信情報を取得できない");
                case "INTERNAL_SERVERERROR":
                    throw new NicoLiveCommentSenderException("内部サーバエラー");
                case "INVALID_MESSAGE":
                    throw new ResponseFormatNicoLiveCommentSenderException("クライアントが送信したコマンドが無効な形式だった、またはリクエスト頻度が高すぎる");
                case "COMMENT_POST_NOT_ALLOWED":
                    throw new ResponseFormatNicoLiveCommentSenderException("コメントの投稿が許可されませんでした、パラメータが不正な可能性があります");
            }
        }

        private bool IsSize(string text)
        {
            return text switch
            {
                "big" or "medium" or "small" => true,
                _ => false,
            };
        }

        private bool IsPosition(string text)
        {
            return text switch
            {
                "ue" or "naka" or "shita" => true,
                _ => false,
            };
        }

        private bool IsFont(string text)
        {
            return text switch
            {
                "defont" or "mincho" or "gothic" => true,
                _ => false,
            };
        }

        private bool IsColor(string text)
        {
            return text switch
            {
                "white" or "red" or "pink" or
                "orange" or "yellow" or "green" or
                "cyan" or "blue" or "purple" or
                "black" => true,
                "white2" or "red2" or "pink2" or
                "orange2" or "yellow2" or "green2" or
                "cyan2" or "blue2" or "purple2" or
                "black2" => true,
                _ => false,
            };
        }

        public void Dispose()
        {
            errorMesColl.Dispose();
            if (clientWebSocket != null) clientWebSocket.Dispose();
        }
    }
}
