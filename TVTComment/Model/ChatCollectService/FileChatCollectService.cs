using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model.ChatCollectService
{
    class FileChatCollectService : OnceASecondChatCollectService
    {
        public override string Name => "ファイル";
        public override ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public override bool CanPost => false;

        private CancellationTokenSource readCancellation = new CancellationTokenSource();
        private readonly bool relativeTime;
        private DateTime? baseTime;
        private readonly Task readTask;
        private readonly NiconicoUtils.NiconicoCommentXmlParser parser = new NiconicoUtils.NiconicoCommentXmlParser(false, null); // 手動ファイルでの自己判定の無効化
        private readonly ConcurrentQueue<NiconicoUtils.ChatAndVpos> chats = new ConcurrentQueue<NiconicoUtils.ChatAndVpos>();

        public override string GetInformationText()
        {
            return $"全コメント数: {chats.Count}  最初のコメントの時刻: {chats.FirstOrDefault()?.Chat.Time.ToString() ?? ""}";
        }

        public FileChatCollectService(ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, StreamReader reader, bool relativeTime) : base(TimeSpan.FromSeconds(10))
        {
            ServiceEntry = serviceEntry;
            this.relativeTime = relativeTime;
            var token = readCancellation.Token;
            readTask = Task.Run(() => Read(reader, token));
        }

        private async Task Read(StreamReader input, CancellationToken token)
        {
            using var reader = input;
            while (!token.IsCancellationRequested && await reader.ReadLineAsync(token).ConfigureAwait(false) is string line)
            {
                parser.Push(line);
                while (parser.DataAvailable())
                {
                    if (parser.Pop() is not NiconicoUtils.ChatNiconicoCommentXmlTag chatTag) continue;
                    chats.Enqueue(new NiconicoUtils.ChatAndVpos(
                        NiconicoUtils.ChatNiconicoCommentXmlTagToChat.Convert(chatTag), chatTag.Vpos
                    ));
                }
            }
        }

        protected override IEnumerable<Chat> GetOnceASecond(ChannelInfo channel, DateTime time)
        {
            if (!baseTime.HasValue)
                baseTime = time;

            if (relativeTime)
            {
                if (chats.IsEmpty) return Array.Empty<Chat>();
                time = chats.First().Chat.Time + (time - baseTime.Value);
            }
            return chats.Where(x => time <= x.Chat.Time && x.Chat.Time < time.AddSeconds(1)).Select(x => x.Chat);
        }

        public override void Dispose()
        {
            using var cancellation = Interlocked.Exchange(ref readCancellation, null);
            cancellation?.Cancel();
            try
            {
                readTask.Wait();
            }
            catch (AggregateException e) when (e.InnerException is OperationCanceledException)
            {
            }
        }

        public override Task PostChat(BasicChatPostObject postObject)
        {
            throw new NotSupportedException();
        }
    }
}
