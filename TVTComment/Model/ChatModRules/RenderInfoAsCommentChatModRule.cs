using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TVTComment.Model.ChatModRules
{
    internal class RenderInfoAsCommentChatModRule : IChatModRule
    {
        private static readonly Regex Pattern = new Regex(@"^/info (?<type>\d+) (?<text>.+)$", RegexOptions.Compiled);

        public string Description => "/info コマンドを運営コメントとして描画";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }

        public RenderInfoAsCommentChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetChatCollectServiceEntries)
        {
            TargetChatCollectServiceEntries = targetChatCollectServiceEntries;
        }

        public bool Modify(Chat chat)
        {
            var match = Pattern.Match(chat.Text);
            if (!match.Success)
            {
                return false;
            }

            var type = int.Parse(match.Groups["type"].Value);
            var text = match.Groups["text"].Value;

            switch (type)
            {
                // /info 2 1人（プレミアム1人）がコミュニティをフォローしました。
                case 2:
                    chat.SetPosition(Chat.PositionType.Top);
                    chat.SetText(text);
                    return true;
                // /info 8 第xx位にランクインしました
                case 8:
                    chat.SetPosition(Chat.PositionType.Top);
                    chat.SetText(text);
                    return true;
                default:
                    chat.SetPosition(Chat.PositionType.Top);
                    chat.SetText(text);
                    return true;
            }
        }
    }
}
