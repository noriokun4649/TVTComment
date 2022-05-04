using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TVTComment.Model.ChatModRules
{
    internal class UrlNgChatModRule : IChatModRule
    {
        private static readonly Regex UrlPattern = new Regex(@"http(s)?://([\w-]+\.)+[\w-]+(/[\w- ./?%&=]*)?", RegexOptions.Compiled);

        public string Description => "URLが含まれるコメをNG";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }

        public UrlNgChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetChatCollectServiceEntries)
        {
            TargetChatCollectServiceEntries = targetChatCollectServiceEntries;
        }

        public bool Modify(Chat chat)
        {
            var matches = UrlPattern.Matches(chat.Text);
            if (matches.Count == 0)
            {
                return false;
            }
            chat.SetNg(true);
            return true;
        }
    }
}
