using System;
using System.Threading.Tasks;

namespace TVTComment.Model
{
    /// <summary>
    /// <seealso cref="IChatTrendService.GetForceValueData"/>で投げる例外
    /// 投げた元の<seealso cref="IChatTrendService"/>は無効化される
    /// </summary>
    public class ChatTrendServiceException : Exception
    {
        public ChatTrendServiceException() { }
        public ChatTrendServiceException(string message) : base(message) { }
        public ChatTrendServiceException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// チャンネルごとのコメントの勢い値を提供するサービス
    /// </summary>
    public interface IChatTrendService : IDisposable
    {
        /// <summary>
        /// ユーザーに表示するサービスの名前
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 値が更新されるであろう間隔、<see cref="GetForceValueData"/>を呼ぶ間隔の目安に使われる
        /// </summary>
        TimeSpan UpdateInterval { get; }

        /// <summary>
        /// 現在の勢い値データを返す
        /// </summary>
        /// <returns>勢い値データ</returns>
        Task<IForceValueData> GetForceValueData();
    }
}
