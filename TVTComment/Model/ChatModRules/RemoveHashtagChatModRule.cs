using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TVTComment.Model.ChatCollectServiceEntry;

namespace TVTComment.Model.ChatModRules
{
    class RemoveHashtagChatModRule : IChatModRule
    {
        private static readonly Regex HashtagPattern = new Regex(@"(?<![\p{L}0-9])([#＃][·・ー_0-9０-９a-zA-Zａ-ｚＡ-Ｚぁ-んァ-ン一-龠]{1,24})(?![\p{L}0-9])", RegexOptions.Compiled);

        public string Description => "ハッシュタグを削除";
        public IEnumerable<IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }

        public RemoveHashtagChatModRule(IEnumerable<IChatCollectServiceEntry> targetChatCollectServiceEntries)
        {
            TargetChatCollectServiceEntries = targetChatCollectServiceEntries;
        }

        public bool Modify(Chat chat)
        {
            var matches = HashtagPattern.Matches(chat.Text);
            if (matches.Count == 0)
            {
                return false;
            }

            var newText = matches
                .OrderByDescending(l => l.Length)
                .Aggregate(chat.Text, (s, match) => s.Replace(match.Value, string.Empty))
                .Trim();
            chat.SetText(newText);

            return true;
        }
    }
}
