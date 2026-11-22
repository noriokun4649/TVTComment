using System.Collections.Generic;

namespace TVTComment.Model.ChatModRules
{
    class UserNgChatModRule : IChatModRule
    {
        public string Description => $"NGUser: {UserId}";
        public IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> TargetChatCollectServiceEntries { get; }
        public string UserId { get; }

        public UserNgChatModRule(IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> targetServiceEntry, string userId)
        {
            TargetChatCollectServiceEntries = targetServiceEntry;
            UserId = userId;
        }

        public bool Modify(Chat chat)
        {
            if (string.IsNullOrWhiteSpace(UserId) || chat.UserId != UserId)
                return false;

            chat.SetNg(true);
            return true;
        }
    }
}
