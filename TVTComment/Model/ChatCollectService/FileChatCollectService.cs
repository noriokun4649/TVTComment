using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TVTComment.Model.ChatCollectService
{
    class FileChatCollectService : OnceASecondChatCollectService
    {
        public override string Name => "ファイル";
        public override ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public override bool CanPost => false;

        private readonly StreamReader reader;
        private readonly bool relativeTime;
        private DateTime? baseTime;
        private readonly Task readTask;
        private readonly NiconicoUtils.NiconicoCommentXmlParser parser = new NiconicoUtils.NiconicoCommentXmlParser(false,""); //ファイルからは自分のコメント判定をしないので空文字
        private readonly ConcurrentQueue<NiconicoUtils.ChatAndVpos> chats = new ConcurrentQueue<NiconicoUtils.ChatAndVpos>();

        public override string GetInformationText()
        {
            return $"全コメント数: {chats.Count}  最初のコメントの時刻: {chats.FirstOrDefault()?.Chat.Time.ToString() ?? ""}";
        }

        public FileChatCollectService(ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, StreamReader reader, bool relativeTime) : base(TimeSpan.FromSeconds(10))
        {
            ServiceEntry = serviceEntry;
            this.reader = reader;
            this.relativeTime = relativeTime;
            readTask = Read();
        }

        private async Task Read()
        {
            while (!reader.EndOfStream)
            {
                string line = await reader.ReadLineAsync();
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
            reader.Dispose();
            try
            {
                readTask.Wait();
            }
            catch (AggregateException e) when (e.InnerException is ObjectDisposedException)
            {
            }
        }

        public override Task PostChat(BasicChatPostObject postObject)
        {
            throw new NotSupportedException();
        }
    }
}
