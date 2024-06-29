using System;

namespace TVTComment.Model.ChatCollectServiceEntry
{
    /// <summary>
    /// <seealso cref="IChatCollectServiceEntry.GetNewService(IChatCollectServiceCreationOption)"/>で投げる例外
    /// </summary>
    public class ChatCollectServiceCreationException : Exception
    {
        public ChatCollectServiceCreationException() { }
        public ChatCollectServiceCreationException(string message) : base(message) { }
        public ChatCollectServiceCreationException(string message, Exception inner) : base(message, inner) { }
    }

    public interface IChatCollectServiceEntry
    {
        ChatService.IChatService Owner { get; }
        string Id { get; }
        string Name { get; }
        string Description { get; }
        /// <summary>
        /// <see cref="GetNewService(IChatCollectServiceCreationOption)"/>でデフォルトオプションを利用できるか
        /// <see langword="true"/>なら引数に<see langword="null"/>を渡せる
        /// </summary>
        bool CanUseDefaultCreationOption { get; }
        /// <summary>
        /// 新しい<see cref="IChatCollectService"/>を作って返す
        /// </summary>
        /// <param name="creationOption"><see cref="IChatCollectService"/>の作成オプション</param>
        /// <returns></returns>
        ChatCollectService.IChatCollectService GetNewService(IChatCollectServiceCreationOption creationOption);
    }
}
