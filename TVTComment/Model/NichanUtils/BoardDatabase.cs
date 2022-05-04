using System.Collections.Generic;
using System.Linq;

namespace TVTComment.Model.NichanUtils
{
    /// <summary>
    /// 2chの板のリストとスレッド選択ルールのデータベース
    /// </summary>
    class BoardDatabase
    {
        public IEnumerable<BoardEntry> BoardList { get; }
        public IEnumerable<ThreadMappingRuleEntry> ThreadMappingRuleEntries { get; }

        public BoardDatabase(IEnumerable<BoardEntry> boardList, IEnumerable<ThreadMappingRuleEntry> threadMappingRuleEntries)
        {
            BoardList = boardList;
            ThreadMappingRuleEntries = threadMappingRuleEntries;
        }

        /// <summary>
        /// <paramref name="channel"/>に対応する<see cref="ThreadMappingRuleEntry"/>を返す
        /// 見つからなければ<c>null</c>
        /// </summary>
        public IEnumerable<ThreadMappingRuleEntry> GetMatchingThreadMappingRuleEntry(ChannelEntry channel)
        {
            foreach (ThreadMappingRuleEntry entry in ThreadMappingRuleEntries)
            {
                switch (entry.Target)
                {
                    case ThreadMappingRuleTarget.Flags:
                        if (((ChannelFlags)entry.Value & channel.Flags) != 0)
                        {
                            yield return entry;
                        }
                        continue;
                    case ThreadMappingRuleTarget.NSId:
                        if (entry.Value == (channel.NetworkId << 16 | channel.ServiceId))
                        {
                            yield return entry;
                        }
                        continue;
                    case ThreadMappingRuleTarget.NId:
                        if (entry.Value == channel.NetworkId)
                        {
                            yield return entry;
                        }
                        continue;
                }
            }
        }

        /// <summary>
        /// 板IDから板を検索 見つからなければ<c>null</c>
        /// </summary>
        public BoardEntry GetBoardEntryById(string id)
        {
            return BoardList.FirstOrDefault(x => x.Id == id);
        }

        /// <summary>
        /// <paramref name="channel"/>に対応する板とスレッド名キーワードを返す
        /// 見つからなければ<c>null</c>
        /// </summary>
        public IEnumerable<MatchingThread> GetMatchingThread(ChannelEntry channel)
        {
            var boardAndThread = GetMatchingThreadMappingRuleEntry(channel).ToList();//板名とスレッドタイトルを得る
            foreach (var entry in boardAndThread)
            {
                BoardEntry board = GetBoardEntryById(entry.BoardId);//板名から板URLと主要スレッド名を得る
                if (board == null)
                    continue;

                var threadTitleKeywords = new List<string>();
                if (entry.ThreadTitleKeywords != null)
                {
                    threadTitleKeywords.AddRange(entry.ThreadTitleKeywords);
                }
                if (board.MainThreadTitleKeywords != null)
                {
                    threadTitleKeywords.AddRange(board.MainThreadTitleKeywords);
                }

                yield return new MatchingThread(board.Title, board.Uri, threadTitleKeywords.ToArray());
            }
        }
    }
}
