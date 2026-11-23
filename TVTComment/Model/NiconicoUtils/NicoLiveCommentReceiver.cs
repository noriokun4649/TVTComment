using System;

namespace TVTComment.Model.NiconicoUtils
{
    class NicoLiveCommentReceiverException : Exception
    {
        public NicoLiveCommentReceiverException() { }
        public NicoLiveCommentReceiverException(string message) : base(message) { }
        public NicoLiveCommentReceiverException(string message, Exception inner) : base(message, inner) { }
    }

    class InvalidPlayerStatusNicoLiveCommentReceiverException : NicoLiveCommentReceiverException
    {
        public string PlayerStatus { get; }
        public InvalidPlayerStatusNicoLiveCommentReceiverException(string playerStatus)
        {
            PlayerStatus = playerStatus;
        }
    }

    class NetworkNicoLiveCommentReceiverException : NicoLiveCommentReceiverException
    {
        public NetworkNicoLiveCommentReceiverException(Exception inner) : base(null, inner)
        {
        }
    }
    class ConnectionClosedNicoLiveCommentReceiverException : NicoLiveCommentReceiverException
    {
        public ConnectionClosedNicoLiveCommentReceiverException()
        {
        }
    }
    class ConnectionDisconnectNicoLiveCommentReceiverException : NicoLiveCommentReceiverException
    {
        public ConnectionDisconnectNicoLiveCommentReceiverException()
        {
        }
    }
}
