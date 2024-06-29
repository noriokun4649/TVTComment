using System;
using System.Collections.Generic;

namespace TVTComment.Model.IPC.IPCMessage
{
    /// <summary>
    /// IPCMessageでデコードに無効な文字列が渡されたときに投げる例外
    /// </summary>
    class IPCMessageDecodeException : ApplicationException
    {
        public IPCMessageDecodeException()
        {
        }
        public IPCMessageDecodeException(string message) : base(message)
        {
        }
        public IPCMessageDecodeException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    interface IIPCMessage
    {
        string MessageName { get; }


        /// <summary>
        /// エンコードされたContentを返す
        /// </summary>
        IEnumerable<string> Encode();

        /// <summary>
        /// Contentをデコードする
        /// </summary>
        void Decode(IEnumerable<string> content);
    }
}
