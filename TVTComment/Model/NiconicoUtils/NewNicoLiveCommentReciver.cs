using Dwango.Nicolive.Chat.Service.Edge;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.Utils;

namespace TVTComment.Model.NiconicoUtils
{
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


        public async IAsyncEnumerable<NiconicoCommentXmlTag> Receive(MessageServer messageServer,[EnumeratorCancellation] CancellationToken cancellationToken)
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

        private async IAsyncEnumerable<T> Retrieve<T>(string uri, MessageParser<T> decoder, [EnumeratorCancellation] CancellationToken cancellationToken) where T : IMessage<T>
        {
            var unread = new List<byte>();
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.IsSuccessStatusCode)
            {
                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[4096];
                int chunk;

                while ((chunk = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    unread.AddRange(buffer[..chunk]);
                    using var memoryStream = new MemoryStream([.. unread]);
                    List<T> messages = [];

                    try
                    {
                        while (memoryStream.Position < memoryStream.Length)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var msg = decoder.ParseDelimitedFrom(memoryStream);
                            messages.Add(msg);
                        }
                        unread.Clear();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (InvalidProtocolBufferException)
                    {
                        //protobufが途中でちぎれていた場合RangeErrorになるので未読分をunreadにつめる
                        unread = unread.Skip((int)memoryStream.Position).ToList(); // Save unread part
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(ex.Message);
                    }

                    foreach (var msg in messages)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        yield return msg;
                    }
                }
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
