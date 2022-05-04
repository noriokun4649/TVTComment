using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TVTComment.Model.TwitterUtils
{
    class SearchWordTable
    {
        private enum RuleTarget { Flags, NSId, NId };
        private readonly List<Tuple<RuleTarget, uint, IEnumerable<string>>> rules = new List<Tuple<RuleTarget, uint, IEnumerable<string>>>();
        private readonly Dictionary<ChannelEntry, string> tableCache = new Dictionary<ChannelEntry, string>();

        public SearchWordTable(string filePath)
        {
            using var reader = new StreamReader(filePath);
            Utils.SimpleCsvReader.ReadByLine(reader, cols =>
            {
                if (cols.Length < 3) return true;
                RuleTarget target;
                switch (cols[0])
                {
                    case "flags": target = RuleTarget.Flags; break;
                    case "nsid": target = RuleTarget.NSId; break;
                    case "nid": target = RuleTarget.NId; break;
                    default: return true;
                }
                rules.Add(new Tuple<RuleTarget, uint, IEnumerable<string>>(target, Utils.PrefixedIntegerParser.ParseToUInt32(cols[1]), cols.Skip(2)));
                return true;
            }, new char[] { '\t' });
        }

        /// <summary>
        /// 対応する検索ワードを返す（対応がなければ空文字列）
        /// </summary>
        public string GetSerchWord(ChannelEntry channel)
        {
            if (tableCache.TryGetValue(channel, out string result))
                return result;
            result = "";
            foreach (Tuple<RuleTarget, uint, IEnumerable<string>> rule in rules)
            {
                switch (rule.Item1)
                {
                    case RuleTarget.Flags:
                        if ((channel.Flags & (ChannelFlags)rule.Item2) != 0)
                        {
                            result = string.Join(" OR ", rule.Item3);
                        }
                        break;
                    case RuleTarget.NSId:
                        if (channel.NetworkId == (rule.Item2 >> 16) && channel.ServiceId == (rule.Item2 & 0xFFFF))
                        {
                            result = string.Join(" OR ", rule.Item3);
                        }
                        break;
                    case RuleTarget.NId:
                        if (channel.NetworkId == rule.Item2)
                        {
                            result = string.Join(" OR ", rule.Item3);
                        }
                        break;
                }
            }

            tableCache.Add(channel, result);
            return result;
        }
    }
}
