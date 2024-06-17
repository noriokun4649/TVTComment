using System;

namespace TVTComment.Model.NiconicoUtils
{
    class NiconicoCommentXmlTag
    {
        /// <summary>
        /// サーバーから取得する場合に受信した時刻を格納する(JST)
        /// </summary>
        public DateTime? ReceivedTime { get; }
        public NiconicoCommentXmlTag(DateTime? receivedTime = null)
        {
            ReceivedTime = receivedTime;
        }
    }
    class ChatNiconicoCommentXmlTag : NiconicoCommentXmlTag
    {
        public string Text { get; }
        public string Thread { get; }
        public int No { get; }
        public int Vpos { get; }
        public long Date { get; }
        public int DateUsec { get; }
        public string Mail { get; }
        public string UserId { get; }
        public int Premium { get; }
        public int Anonymity { get; }
        public int Abone { get; }
        public bool SelfComment { get; }

        public ChatNiconicoCommentXmlTag(
            string text, string thread, int no, int vpos, long date, int dateUsec,
            string mail, string userId, int premium, int anonymity, int abone, bool selfComment
        )
        {
            Text = text;
            Thread = thread;
            No = no;
            Vpos = vpos;
            Date = date;
            DateUsec = dateUsec;
            Mail = mail;
            UserId = userId;
            Premium = premium;
            Anonymity = anonymity;
            Abone = abone;
            SelfComment = selfComment;
        }
    }
    class ThreadNiconicoCommentXmlTag : NiconicoCommentXmlTag
    {
        /// <summary>
        /// スレッドができた日付時刻をUNIX時刻JSTで表現したもの
        /// </summary>
        public ulong Thread { get; }
        public string Ticket { get; }
        public ulong ServerTime { get; }
        public ThreadNiconicoCommentXmlTag(DateTime receivedTime, ulong thread, string ticket, ulong serverTime) : base(receivedTime)
        {
            Thread = thread;
            Ticket = ticket;
            ServerTime = serverTime;
        }
    }
    class ChatResultNiconicoCommentXmlTag : NiconicoCommentXmlTag
    {
        public int Status { get; }
        public ChatResultNiconicoCommentXmlTag(int status)
        {
            Status = status;
        }
    }
    class LeaveThreadNiconicoCommentXmlTag : NiconicoCommentXmlTag { }

}
