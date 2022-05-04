using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TVTComment.Model.ChatModRules
{
    internal class ReplaceRegexChatModRule : IChatModRule
    {
        public string Description => $"正規表現を置換: \"{Regex}\" → \"{Replacement}\"";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }
        public string Regex { get; }
        private Regex CompiledRegex { get; }
        public string Replacement { get; }

        public ReplaceRegexChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetChatCollectServiceEntries, string regex, string replacement)
        {
            TargetChatCollectServiceEntries = targetChatCollectServiceEntries;

            Regex = regex;
            CompiledRegex = new Regex(regex, RegexOptions.Compiled);
            Replacement = replacement;
        }

        public bool Modify(Chat chat)
        {
            var newText = CompiledRegex.Replace(chat.Text, Replacement);
            if (chat.Text == newText)
            {
                return false;
            }

            chat.SetText(newText);
            return true;
        }
    }
}
