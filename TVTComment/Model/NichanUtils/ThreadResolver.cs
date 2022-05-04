using System.Collections.Generic;

namespace TVTComment.Model.NichanUtils
{
    /// <summary>
    /// <see cref="ChannelInfo"/>から対応する板URL・スレッドタイトルキーワードを得る
    /// </summary>
    class ThreadResolver
    {
        private readonly ChannelDatabase channelDatabase;
        private readonly BoardDatabase boardDatabase;

        public ThreadResolver(ChannelDatabase channelDatabase, BoardDatabase boardDatabase)
        {
            this.channelDatabase = channelDatabase;
            this.boardDatabase = boardDatabase;
        }

        public IEnumerable<MatchingThread> Resolve(ChannelInfo channelInfo, bool ignoreMainThreadTitleKeywords)
        {
            IEnumerable<MatchingThread> getMatchingThread(ChannelEntry channel)
            {
                if (!ignoreMainThreadTitleKeywords)
                    foreach (var entry in boardDatabase.GetMatchingThread(channel))
                    {
                        yield return entry;
                    }
                else
                {
                    IEnumerable<ThreadMappingRuleEntry> ruleEntry = boardDatabase.GetMatchingThreadMappingRuleEntry(channel);
                    foreach (var entry in ruleEntry)
                    {
                        BoardEntry boardEntry = boardDatabase.GetBoardEntryById(entry.BoardId);
                        if (boardEntry == null) continue;
                        yield return new MatchingThread(boardEntry.Title, boardEntry.Uri, entry.ThreadTitleKeywords);
                    }
                }
            }

            ushort networkId = channelInfo.NetworkId, serviceId = channelInfo.ServiceId;
            if (networkId == 0)
            {
                //録画ファイルなどではネットワークIDが分からないのでサービスIDだけで検索
                //BSとCSの間ではサービスIDが重複する可能性があるがほとんどないので割り切る
                foreach (ChannelEntry channel in channelDatabase.GetByServiceId(serviceId))
                {
                    IEnumerable<MatchingThread> ret = getMatchingThread(channel);
                    return ret;
                }
                return null;
            }
            else
            {
                ChannelEntry channel = channelDatabase.GetByNetworkIdAndServiceId(networkId, serviceId);//channels.txtの登録チャンネルに解決
                if (channel == null)
                    return null;
                return getMatchingThread(channel);
            }
        }
    }
}
