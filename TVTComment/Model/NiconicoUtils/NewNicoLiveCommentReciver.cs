using Dwango.Nicolive.Chat.Service.Edge;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.Utils;

namespace TVTComment.Model.NiconicoUtils
{
    /**
     * 
     * This class is derived from integration-docs/example
     * See THIRDPARTY_LICENSE.
     * 
    **/
    class NewNicoLiveCommentReciver : IDisposable
    {
        public NiconicoLoginSession NiconicoLoginSession { get; }
        private readonly HttpClient httpClient;
        private NiconicoCommentProtobufParser parser;

        public NewNicoLiveCommentReciver(NiconicoLoginSession niconicoLoginSession)
        {
            NiconicoLoginSession = niconicoLoginSession;
            var handler = new HttpClientHandler();
            handler.CookieContainer.Add(niconicoLoginSession.Cookie);
            httpClient = new HttpClient(handler);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", MiscUtils.GetUserAgent());
        }


        public async IAsyncEnumerable<NiconicoCommentXmlTag> Receive(MessageServer messageServer,[EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var hashedUserId = messageServer.HashedUserId;
            var viewUri = messageServer.ViewUri;

            parser = new(hashedUserId);
            long? next = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            while (next.HasValue && !cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fetchUri = $"{viewUri}?at={next.Value}";
                next = null;

                await foreach (var entry in Retrieve(fetchUri, ChunkedEntry.Parser, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if(cancellationToken.IsCancellationRequested) next = null;

                    if (entry.Segment != null)
                    {
                        await foreach (var msg in Retrieve(entry.Segment.Uri, ChunkedMessage.Parser, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                        {
                            if (cancellationToken.IsCancellationRequested) next = null;
                            parser.Push(msg);
                            if (msg.State != null)
                            {
                                UpdateState(msg);
                            }
                            while (parser.DataAvailable()) {
                                yield return parser.Pop();
                            }
                        }
                    }
                    else if (entry.Next != null)
                    {
                        next = entry.Next.At;
                    }
                }
            }
        }

        private async IAsyncEnumerable<T> Retrieve<T>(string uri, MessageParser<T> decoder, [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : IMessage<T>
        {
            var unread = new List<byte>();
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.IsSuccessStatusCode)
            {
                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[4096];
                int chunk;

                while ((chunk = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0 && !cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    unread.AddRange(buffer[..chunk]);
                    int position = 0;

                    while (position < unread.Count)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int payloadOffset = position;
                        uint length = 0;
                        bool hasLength = false;

                        // 長さと本文が揃うまで未完のメッセージを保持
                        for (int shift = 0; payloadOffset < unread.Count; shift += 7)
                        {
                            byte value = unread[payloadOffset++];
                            if (shift == 28 && (value & 0xf8) != 0)
                                throw new InvalidDataException("コメントデータのメッセージ長が不正です。");
                            length |= (uint)(value & 0x7f) << shift;
                            if ((value & 0x80) == 0)
                            {
                                hasLength = true;
                                break;
                            }
                        }

                        if (!hasLength || length > unread.Count - payloadOffset)
                            break;

                        var msg = decoder.ParseFrom(unread.GetRange(payloadOffset, (int)length).ToArray());
                        position = payloadOffset + (int)length;
                        yield return msg;
                    }
                    unread.RemoveRange(0, position);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (unread.Count != 0)
                    throw new EndOfStreamException("コメントデータの受信が途中で終了しました。");
            }
        }

        private void UpdateState(ChunkedMessage msg)
        {
            // 現在は不要だが、将来状態更新するときここに書く
        }


        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}
