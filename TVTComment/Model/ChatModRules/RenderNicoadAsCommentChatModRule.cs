using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TVTComment.Model.ChatModRules
{
    internal class RenderNicoadAsCommentChatModRule : IChatModRule
    {
        private static readonly Regex Pattern = new Regex(@"^/nicoad \{""totalAdPoint"":\d+,""message"":""(?<message>.+)"",""version"":""1""\}$", RegexOptions.Compiled);

        public string Description => "/nicoad コマンドを運営コメントとして描画";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }

        public RenderNicoadAsCommentChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetChatCollectServiceEntries)
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

            // /nicoad {"totalAdPoint":4600,"message":"【広告貢献1位】xxxさんが3100ptニコニ広告しました","version":"1"}
            var message = match.Groups["message"].Value;

            chat.SetText(message);
            chat.SetPosition(Chat.PositionType.Top);

            return true;

        }
    }
}
