using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TVTComment.Model.ChatModRules
{
    internal class AnchorNgChatModRule : IChatModRule
    {
        private static readonly Regex AnchorPattern = new Regex(@">>\d+([-,]\d+)?", RegexOptions.Compiled);

        public string Description => "アンカーが含まれるコメをNG";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }

        public AnchorNgChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetChatCollectServiceEntries)
        {
            TargetChatCollectServiceEntries = targetChatCollectServiceEntries?.ToArray();
        }

        public bool Modify(Chat chat)
        {
            // アンカー削除などの改変ルールの適用順に依存しない、改変前本文での判定
            if (!AnchorPattern.IsMatch(chat.OriginalText))
                return false;

            chat.SetNg(true);
            return true;
        }
    }
}
