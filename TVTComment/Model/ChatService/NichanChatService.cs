using ObservableUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TVTComment.Model.ChatService
{
    class NichanChatServiceSettings
    {
        public class GochanApiSettings
        {
        }

        public TimeSpan ThreadUpdateInterval { get; set; } = new TimeSpan(0, 0, 1);
        public TimeSpan ThreadListUpdateInterval { get; set; } = new TimeSpan(0, 0, 15);
        public TimeSpan PastCollectServiceBackTime { get; set; } = new TimeSpan(3, 0, 0);
        public string PastUserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36";
        public GochanApiSettings GochanApi { get; set; } = new GochanApiSettings();

        // 以下は旧設定移行用
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string HmKey { get; set; } = null;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string AppKey { get; set; } = null;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string UserId { get; set; } = null;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Password { get; set; } = null;
    }

    class NichanChatService : IChatService
    {
        public class BoardInfo
        {
            public string Title { get; }
            public Uri Uri { get; }
            public BoardInfo(string title, Uri uri)
            {
                Title = title;
                Uri = uri;
            }
        }

        public string Name => "2ch";
        public IReadOnlyList<ChatCollectServiceEntry.IChatCollectServiceEntry> ChatCollectServiceEntries { get; }
        public IReadOnlyList<IChatTrendServiceEntry> ChatTrendServiceEntries { get; }
        public IEnumerable<BoardInfo> BoardList { get; }

        public TimeSpan ResCollectInterval => resCollectInterval.Value;
        public TimeSpan ThreadSearchInterval => threadSearchInterval.Value;
        public string GochanPastUserAgent => pastUserAgent.Value;
        public TimeSpan PastCollectServiceBackTime => pastCollectServiceBackTime.Value;

        //このChatServiceに行われた設定変更が子のChatServiceEntryに伝わるようにするためにObservableValueで包む
        private readonly ObservableValue<TimeSpan> resCollectInterval = new ObservableValue<TimeSpan>();
        private readonly ObservableValue<TimeSpan> threadSearchInterval = new ObservableValue<TimeSpan>();
        private readonly ObservableValue<Nichan.ApiClient> nichanApiClient = new ObservableValue<Nichan.ApiClient>();
        private readonly ObservableValue<TimeSpan> pastCollectServiceBackTime = new ObservableValue<TimeSpan>();
        private readonly ObservableValue<string> pastUserAgent = new ObservableValue<string>();

        private readonly NichanUtils.BoardDatabase boardDatabase;
        private readonly NichanUtils.ThreadResolver threadResolver;

        private readonly NichanChatServiceSettings settings;

        public NichanChatService(
            NichanChatServiceSettings settings, ChannelDatabase channelDatabase,
            string threadSettingFilePath
        )
        {
            this.settings = settings;
            

            var boardSetting = NichanUtils.ThreadSettingFileParser.Parse(threadSettingFilePath);
            boardDatabase = new NichanUtils.BoardDatabase(boardSetting.BoardEntries, boardSetting.ThreadMappingRuleEntries);
            threadResolver = new NichanUtils.ThreadResolver(channelDatabase, boardDatabase);

            resCollectInterval.Value = settings.ThreadUpdateInterval;
            threadSearchInterval.Value = settings.ThreadListUpdateInterval;
            nichanApiClient.Value = new Nichan.ApiClient();
            pastCollectServiceBackTime.Value = settings.PastCollectServiceBackTime;
            pastUserAgent.Value = settings.PastUserAgent;

            ChatCollectServiceEntries = new ChatCollectServiceEntry.IChatCollectServiceEntry[] {
                new ChatCollectServiceEntry.DATNichanChatCollectServiceEntry(this, resCollectInterval, threadSearchInterval, threadResolver, nichanApiClient),
                new ChatCollectServiceEntry.PastNichanChatCollectServiceEntry(this, threadResolver, pastCollectServiceBackTime, pastUserAgent),
            };
            ChatTrendServiceEntries = Array.Empty<IChatTrendServiceEntry>();

            BoardList = boardDatabase.BoardList.Select(x => new BoardInfo(x.Title, x.Uri));
        }

        public void SetIntervalValues(TimeSpan resCollectInterval, TimeSpan threadSearchInterval)
        {
            settings.ThreadUpdateInterval = resCollectInterval;
            settings.ThreadListUpdateInterval = threadSearchInterval;

            this.resCollectInterval.Value = resCollectInterval;
            this.threadSearchInterval.Value = threadSearchInterval;
        }
        
        public void SetPastUserAgent(string val) { 
            settings.PastUserAgent = val;
            pastUserAgent.Value = val;
        }

        public void SetPastCollectServiceBackTime(TimeSpan value)
        {
            settings.PastCollectServiceBackTime = value;
            pastCollectServiceBackTime.Value = value;
        }

        public void Dispose()
        {
        }
    }
}
