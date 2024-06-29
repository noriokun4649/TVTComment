using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TVTComment.Model.ChatCollectService
{
    /// <summary>
    /// <seealso cref="IChatCollectService.GetChats(ChannelInfo, DateTime)"/>で投げる例外
    /// 投げた元の<seealso cref="IChatCollectService"/>は無効化される
    /// </summary>
    public class ChatCollectException : Exception
    {
        public ChatCollectException() { }
        public ChatCollectException(string message) : base(message) { }
        public ChatCollectException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// <seealso cref="IChatCollectService.PostChat(BasicChatPostObject)"/>で投げる例外
    /// </summary>
    public class ChatPostException : Exception
    {
        public ChatPostException() { }
        public ChatPostException(string message) : base(message) { }
        public ChatPostException(string message, Exception inner) : base(message, inner) { }
    }

    public class ChatPostSessionException : Exception
    {
        public ChatPostSessionException() { }
        public ChatPostSessionException(string message) : base(message) { }
        public ChatPostSessionException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// 投稿するコメントを表すオブジェクトのベース
    /// </summary>
    public class BasicChatPostObject
    {
        public string Text { get; }
        public BasicChatPostObject(string text)
        {
            Text = text;
        }
    }

    public interface IChatCollectService : IDisposable
    {
        /// <summary>
        /// ユーザーに表示する名前
        /// </summary>
        string Name { get; }

        /// <summary>
        /// ユーザーに表示する現在の状態を返す
        /// </summary>
        string GetInformationText();

        /// <summary>
        /// 自分がどの<seealso cref="ChatCollectServiceEntry.IChatCollectServiceEntry"/>から生み出されたか
        /// </summary>
        ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }

        /// <summary>
        /// コメントを取得して返す
        /// </summary>
        /// <param name="channel">コメントを取得したい対象チャンネル</param>
        /// <param name="time">コメントを取得したい対象時刻</param>
        /// <returns>取得したコメント</returns>
        IEnumerable<Chat> GetChats(ChannelInfo channel,EventInfo events , DateTime time);

        /// <summary>
        /// コメント投稿に対応しているか
        /// falseなら<see cref="PostChat"/>を呼んではならない
        /// </summary>
        bool CanPost { get; }

        /// <summary>
        /// コメントを投稿する
        /// </summary>
        /// <param name="postObject">投稿したいコメントを表すオブジェクト</param>
        Task PostChat(BasicChatPostObject postObject);
    }
}
