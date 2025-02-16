namespace TVTComment.Model
{
    class TVTCommentSettings
    {
        public ChatService.NiconicoChatServiceSettings Niconico { get; set; } = new ChatService.NiconicoChatServiceSettings();
        public ChatService.NichanChatServiceSettings Nichan { get; set; } = new ChatService.NichanChatServiceSettings();
        public ChatService.TwitterChatServiceSettings Twitter { get; set; } = new ChatService.TwitterChatServiceSettings();
        public string[] ChatPostMailTextExamples { get; set; } = System.Array.Empty<string>();
        public string ChatPostSplitterLeftSize { get; set; } = "*";
        public string ChatPostSplitterRightSize { get; set; } = "3*";
        public byte ChatOpacity { get; set; } = 255;
        public int ChatPreserveCount { get; set; } = 10000;
        public bool ClearChatsOnChannelChange { get; set; } = false;
        public bool NgCommentNotShow { get; set; } = false;
        public bool AddChannelLogOnChannelChange { get; set; } = false;
        public bool NXJikkyoImportDisable { get; set; } = false;
        public bool UiFlashingDeterrence { get; set; } = false;
        public Serialization.ChatModRuleEntity[] ChatModRules { get; set; } = System.Array.Empty<Serialization.ChatModRuleEntity>();
        public bool UseDefaultChatCollectService { get; set; } = false;
        public string[] LiveDefaultChatCollectServices { get; set; } = System.Array.Empty<string>();
        public string[] RecordDefaultChatCollectServices { get; set; } = System.Array.Empty<string>();
        public ViewModels.ViewSettings View { get; set; } = new ViewModels.ViewSettings();
    }

}
