namespace TVTComment.ViewModels.Notifications
{
    /// <summary>
    /// コメント元を追加するときに表示されるダイアログ<see cref="ChatCollectServiceCreationSettingsControlViewModel"/>に渡す情報
    /// </summary>
    class ChatCollectServiceCreationSettingsConfirmation
    {
        public string Title { get; set; }
        public bool Confirmed { get; set; }
        /// <summary>
        /// どのコメント元を追加するか
        /// </summary>
        public Model.ChatCollectServiceEntry.IChatCollectServiceEntry TargetChatCollectServiceEntry { get; set; }
        /// <summary>
        /// ダイアログの結果として得た<see cref="Model.IChatCollectServiceCreationOption"/>
        /// </summary>
        public Model.ChatCollectServiceEntry.IChatCollectServiceCreationOption ChatCollectServiceCreationOption { get; set; }
    }
}
