using TVTComment.Model.NiconicoUtils;

namespace TVTComment.Model.ChatCollectServiceEntry
{
    sealed class NicoJCatchChatCollectServiceEntry(ChatService.IChatService owner, JkIdResolver resolver,
        ChannelDatabase channels, ChatService.NiconicoChatServiceSettings settings) : IChatCollectServiceEntry
    {
        public ChatService.IChatService Owner { get; } = owner;
        public string Id => "NicoJCatch";
        public string Name => "NicojCatch過去ログ";
        public string Description => "設定した親フォルダから局・再生時刻に対応するNicojCatchの過去ログを自動読み込み";
        public bool CanUseDefaultCreationOption => true;

        public ChatCollectService.IChatCollectService GetNewService(IChatCollectServiceCreationOption creationOption) =>
            new ChatCollectService.NicoJCatchChatCollectService(this, resolver, channels, settings);
    }
}
