using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TVTComment.Model.ChatModRules
{
    internal class RenderEmotionAsCommentChatModRule : IChatModRule
    {
        private const int MaxExpandedTextLength = 4096;
        private static readonly Regex Pattern = new Regex("^/emotion (?<text>.+)$", RegexOptions.Compiled);
        private static readonly Regex CountPattern = new Regex(@"^/emotion (?<text>.+)×(?<count>\d+)$", RegexOptions.Compiled);

        public string Description => "/emotion コマンドを通常コメントとして描画";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }

        public RenderEmotionAsCommentChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetChatCollectServiceEntries)
        {
            TargetChatCollectServiceEntries = targetChatCollectServiceEntries;
        }

        public bool Modify(Chat chat)
        {
            var countMatch = CountPattern.Match(chat.Text);
            if (countMatch.Success)
            {
                var text = countMatch.Groups["text"].Value;
                // 乗算のオーバーフローを避けて、展開後のUTF-16長を制限する。
                if (!int.TryParse(countMatch.Groups["count"].Value, out var n) || n > MaxExpandedTextLength / text.Length)
                    return false;

                var newText = string.Concat(Enumerable.Repeat(text, n));
                chat.SetText(newText);
                return true;
            }

            var match = Pattern.Match(chat.Text);
            if (match.Success)
            {
                var newText = match.Groups["text"].Value;
                chat.SetText(newText);
                return true;
            }

            return false;
        }
    }
}
