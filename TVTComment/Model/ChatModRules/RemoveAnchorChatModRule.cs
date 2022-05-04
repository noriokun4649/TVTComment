using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TVTComment.Model.ChatModRules
{
    internal class RemoveAnchorChatModRule : IChatModRule
    {
        private static readonly Regex AnchorPattern = new Regex(@">>\d+([-,]\d+)?", RegexOptions.Compiled);

        public string Description => "アンカーを削除";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }

        public RemoveAnchorChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetChatCollectServiceEntries)
        {
            TargetChatCollectServiceEntries = targetChatCollectServiceEntries;
        }

        public bool Modify(Chat chat)
        {
            var matches = AnchorPattern.Matches(chat.Text);
            if (matches.Count == 0)
            {
                return false;
            }

            var newText = matches
                .Aggregate(chat.Text, (s, match) => s.Replace(match.Value, string.Empty))
                .Trim();
            chat.SetText(newText);

            return true;
        }
    }
}
