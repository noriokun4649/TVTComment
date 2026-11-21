using System;
using System.Collections.Generic;
using System.IO;

namespace TVTComment.Model
{
    /// <summary>
    /// チャンネルの付加情報フラグ（主に放送局系列情報）
    /// </summary>
    [Flags]
    enum ChannelFlags : ushort
    {
        /// <summary>
        /// NHK総合
        /// </summary>
        NHK = 1,
        /// <summary>
        /// NHKEテレ
        /// </summary>
        ETV = 2,
        /// <summary>
        /// 日本テレビ系
        /// </summary>
        NTV = 4,
        /// <summary>
        /// TBSテレビ系
        /// </summary>
        TBS = 8,
        /// <summary>
        /// フジテレビ系
        /// </summary>
        CX = 16,
        /// <summary>
        /// テレビ朝日系
        /// </summary>
        EX = 32,
        /// <summary>
        /// テレビ東京系
        /// </summary>
        TX = 64,
    }

    /// <summary>
    /// channels.txtに記載された登録チャンネルを表す
    /// </summary>
    class ChannelEntry
    {
        /// <summary>
        /// ネットワークID ただし地上波は0xF
        /// </summary>
        public ushort NetworkId { get; }
        /// <summary>
        /// サービスID
        /// </summary>
        public ushort ServiceId { get; }
        /// <summary>
        /// 放送地域 ただし衛星放送はnull
        /// </summary>
        public string Region { get; }
        /// <summary>
        /// チャンネル名
        /// </summary>
        public string Name { get; }
        public ChannelFlags Flags { get; }

        public ChannelEntry(ushort networkId, ushort serviceId, string region, string name, ChannelFlags flags)
        {
            NetworkId = networkId;
            ServiceId = serviceId;
            Region = region;
            Name = name;
            Flags = flags;
        }
    }

    class ChannelDatabase
    {
        private class ChannelComparer : Comparer<ChannelEntry>
        {
            public override int Compare(ChannelEntry x, ChannelEntry y)
            {
                //まずサービスIDで比較し同じならネットワークIDで比較する
                int tmp = x.ServiceId.CompareTo(y.ServiceId);
                if (tmp != 0)
                    return tmp;
                return x.NetworkId.CompareTo(y.NetworkId);
            }
        }
        private static readonly ChannelComparer channelComparer = new ChannelComparer();

        private readonly List<ChannelEntry> channelList = new List<ChannelEntry>();//効率化のためにサービスID,ネットワークIDの順でソートしておく
        public IReadOnlyList<ChannelEntry> ChannelList => channelList;

        public ChannelDatabase(string dataFilePath)
        {
            using StreamReader reader = new StreamReader(dataFilePath);
            Utils.SimpleCsvReader.ReadByLine(reader, cols =>
             {
                 if (cols.Length != 5) return true;

                 ushort nid = Utils.PrefixedIntegerParser.ParseToUInt16(cols[0]);
                 if (nid == 0)
                     throw new Exception();
                 var newItem = new ChannelEntry(nid, Utils.PrefixedIntegerParser.ParseToUInt16(cols[1]), cols[2] == "*" ? null : cols[2], cols[3], (ChannelFlags)Utils.PrefixedIntegerParser.ParseToUInt16(cols[4]));
                 int idx = channelList.BinarySearch(newItem, channelComparer);
                 if (idx < 0) idx = ~idx;//サービスIDが被っている
                 channelList.Insert(idx, newItem);
                 return true;
             }, new char[] { '\t' });
        }

        /// <summary>
        /// ネットワークIDとサービスIDから登録チャンネルを検索する
        /// </summary>
        /// <param name="networkId">ネットワークID 地上波の場合0xFでもいい</param>
        /// <param name="serviceId">サービスID</param>
        public ChannelEntry GetByNetworkIdAndServiceId(ushort networkId, ushort serviceId)
        {
            if (networkId == 0) throw new ArgumentOutOfRangeException(nameof(networkId), networkId, $"{nameof(networkId)} must not be 0");

            if (IsTerrestrial(networkId))
                networkId = 0xF;

            int idx = channelList.BinarySearch(new ChannelEntry(networkId, serviceId, null, null, 0), channelComparer);
            if (idx < 0)
                return null;
            return channelList[idx];
        }

        public IEnumerable<ChannelEntry> GetByServiceId(ushort serviceId)
        {
            int idx = ~channelList.BinarySearch(new ChannelEntry(0, serviceId, null, null, 0), channelComparer);

            var ret = new List<ChannelEntry>();
            for (; idx < channelList.Count && channelList[idx].ServiceId == serviceId; idx++)
            {
                ret.Add(channelList[idx]);
            }

            return ret;
        }

        /// <summary>
        /// ネットワークIDが地上波範囲かどうかを返す
        /// </summary>
        public static bool IsTerrestrial(ushort networkId)
        {
            return 0x7880 <= networkId && networkId <= 0x7fe8;
        }
    }
}
