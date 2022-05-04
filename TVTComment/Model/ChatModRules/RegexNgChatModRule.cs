using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TVTComment.Model.ChatModRules
{
    internal class RegexNgChatModRule : IChatModRule
    {
        public string Description => $"正規表現をNG: \"{Regex}\"";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }
        public string Regex { get; }
        private Regex CompiledRegex { get; }

        public RegexNgChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetChatCollectServiceEntries, string regex)
        {
            TargetChatCollectServiceEntries = targetChatCollectServiceEntries;

            Regex = regex;
            CompiledRegex = new Regex(regex, RegexOptions.Compiled);
        }

        public bool Modify(Chat chat)
        {
            if (CompiledRegex.Matches(chat.Text).Any())
            {
                chat.SetNg(true);
                return true;
            }

            return false;
        }
    }
}
