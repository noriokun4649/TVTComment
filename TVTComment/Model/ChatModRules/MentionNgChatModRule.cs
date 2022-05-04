using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TVTComment.Model.ChatCollectServiceEntry;

namespace TVTComment.Model.ChatModRules
{
    class MentionNgChatModRule : IChatModRule
    {
        private static readonly Regex MentionPattern = new Regex("@[0-9a-zA-Z_]{1,15}", RegexOptions.Compiled);

        public string Description => "メンションが含まれるコメをNG";
        public IEnumerable<IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }

        public MentionNgChatModRule(IEnumerable<IChatCollectServiceEntry> targetChatCollectServiceEntries)
        {
            TargetChatCollectServiceEntries = targetChatCollectServiceEntries;
        }

        public bool Modify(Chat chat)
        {
            var matches = MentionPattern.Matches(chat.Text);
            if (matches.Count == 0)
            {
                return false;
            }
            chat.SetNg(true);
            return true;
        }
    }
}