using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TVTComment.Model.ChatModRules
{
    internal class RemoveUrlChatModRule : IChatModRule
    {
        private static readonly Regex UrlPattern = new Regex(@"http(s)?://([\w-]+\.)+[\w-]+(/[\w./?%&=-]*)?", RegexOptions.Compiled);

        public string Description => "URL を削除";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }

        public RemoveUrlChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetChatCollectServiceEntries)
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

            var newText = matches
                .Aggregate(chat.Text, (s, match) => s.Replace(match.Value, string.Empty))
                .Trim();
            chat.SetText(newText);

            return true;
        }
    }
}
