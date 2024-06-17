using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace TVTComment.Model.NiconicoUtils
{
    class NiconicoCommentXmlParser
    {
        private readonly bool socketFormat;
        private bool inChatTag;
        private bool inThreadTag;
        private readonly Queue<NiconicoCommentXmlTag> chats = new Queue<NiconicoCommentXmlTag>();
        private string buffer;
        private readonly string myUserId;

        /// <summary>
        /// <see cref="NiconicoCommentXmlParser"/>を初期化する
        /// </summary>
        /// <param name="socketFormat">ソケットを使うリアルタイムのデータ形式ならtrue 過去ログなどのデータ形式ならfalse</param>
        public NiconicoCommentXmlParser(bool socketFormat,string myUserId)
        {
            this.socketFormat = socketFormat;
            this.myUserId = myUserId;
            inChatTag = false;
            inThreadTag = false;
        }

        public void Push(string str)
        {
            buffer += str;

            if (socketFormat)
            {
                string[] tmp = buffer.Split('\0');
                foreach (string tagStr in tmp.Take(tmp.Length - 1))
                {
                    if (tagStr.StartsWith("<chat_result"))
                    {
                        int idx = tagStr.IndexOf("status=") + 8;
                        int status = int.Parse(tagStr[idx..tagStr.IndexOf('"', idx)]);
                        chats.Enqueue(new ChatResultNiconicoCommentXmlTag(status));
                    }
                    else if (tagStr.StartsWith("<chat"))
                    {
                        chats.Enqueue(GetChatXmlTag(tagStr, myUserId));
                    }
                    else if (tagStr.StartsWith("<thread"))
                    {
                        chats.Enqueue(GetThreadXmlTag(tagStr));
                    }
                    else if (tagStr.StartsWith("<leave_thread"))
                    {
                        chats.Enqueue(new LeaveThreadNiconicoCommentXmlTag());
                    }
                }
                buffer = tmp[^1];
            }
            else
            {
                while (true)
                {
                    if (inChatTag)
                    {
                        int idx = buffer.IndexOf("</chat>");
                        if (idx == -1) break;
                        idx += 7;
                        string tagStr = buffer.Substring(0, idx);
                        buffer = buffer[idx..];
                        inChatTag = false;
                        chats.Enqueue(GetChatXmlTag(tagStr, myUserId));
                    }
                    else if (inThreadTag)
                    {
                        int idx = buffer.IndexOf("/>");
                        if (idx == -1) break;
                        idx += 2;
                        string tagStr = buffer.Substring(0, idx);
                        buffer = buffer[idx..];
                        inThreadTag = false;
                        chats.Enqueue(GetThreadXmlTag(tagStr));
                    }
                    else
                    {
                        int idx = buffer.IndexOf('<');
                        if (idx == -1) break;

                        buffer = buffer[idx..];
                        if (buffer.StartsWith("<chat"))
                            inChatTag = true;
                        else if (buffer.StartsWith("<thread"))
                            inThreadTag = true;
                        else
                        {
                            if (buffer.Length > 0)
                                buffer = buffer[1..];
                            else
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 解析結果を返す <see cref="socketFormat"/>がfalseなら<see cref="ChatNiconicoCommentXmlTag"/>しか返さない
        /// </summary>
        /// <returns>解析結果の<see cref="NiconicoCommentXmlTag"/></returns>
        public NiconicoCommentXmlTag Pop()
        {
            return chats.Dequeue();
        }

        /// <summary>
        /// <see cref="Pop"/>で読みだすデータがあるか
        /// </summary>
        public bool DataAvailable()
        {
            return chats.Count > 0;
        }

        public void Reset()
        {
            inChatTag = false;
            inThreadTag = false;
            buffer = string.Empty;
            chats.Clear();
        }

        private static readonly Regex reChat = new Regex("<chat(?= )(.*)>(.*?)</chat>", RegexOptions.Singleline);
        private static readonly Regex reThread = new Regex("thread=\"(\\d+)\"");
        private static readonly Regex reDate = new Regex("date=\"(\\d+)\"");
        private static readonly Regex reDateUsec = new Regex("date_usec=\"(\\d+)\"");
        private static readonly Regex reMail = new Regex(" mail=\"(.*?)\"");
        private static readonly Regex reUserID = new Regex(" user_id=\"([0-9A-Za-z\\-_]{0,27})");
        private static readonly Regex rePremium = new Regex(" abone=\"(\\d+)\"");
        private static readonly Regex reAnonymity = new Regex(" abone=\"(\\d+)\"");
        private static readonly Regex reAbone = new Regex(" abone=\"(\\d+)\"");

        private static ChatNiconicoCommentXmlTag GetChatXmlTag(string str,string myUserId)
        {
            string text = HttpUtility.HtmlDecode(reChat.Match(str).Groups[2].Value);

            Match match = reThread.Match(str);
            string thread = match.Success ? HttpUtility.HtmlDecode(match.Groups[1].Value) : "";

            long date = long.Parse(reDate.Match(str).Groups[1].Value);

            match = reDateUsec.Match(str);
            int dateUsec = match.Success ? int.Parse(match.Groups[1].Value) : 0;

            match = reMail.Match(str);
            string mail = match.Success ? HttpUtility.HtmlDecode(match.Groups[1].Value) : "";

            string userId = reUserID.Match(str).Groups[1].Value;

            match = rePremium.Match(str);
            int premium = match.Success ? int.Parse(match.Groups[1].Value) : 0;

            match = reAnonymity.Match(str);
            int anonymity = match.Success ? int.Parse(match.Groups[1].Value) : 0;

            match = reAbone.Match(str);
            int abone = match.Success ? int.Parse(match.Groups[1].Value) : 0;

            return new ChatNiconicoCommentXmlTag(text, thread, GetNumberFromChatTag(str), GetVposFromChatTag(str), date, dateUsec, mail, userId, premium, anonymity, abone, userId.Equals(myUserId));
        }

        private static readonly Regex reNo = new Regex(" no=\"(\\d+)\"");
        private static int GetNumberFromChatTag(string str)
        {
            var match = reNo.Match(str);
            if (match.Success)
                return int.Parse(match.Groups[1].Value);
            else
                return 0;
        }

        private static readonly Regex reVpos = new Regex(@"vpos=""(-?\d+)""");
        private static int GetVposFromChatTag(string str)
        {
            return int.Parse(reVpos.Match(str).Groups[1].Value);
        }

        private static ThreadNiconicoCommentXmlTag GetThreadXmlTag(string tagStr)
        {
            int idx = tagStr.IndexOf("thread=") + 8;
            ulong thread = ulong.Parse(tagStr[idx..tagStr.IndexOf('"', idx)]);
            idx = tagStr.IndexOf("ticket=") + 8;
            string ticket = tagStr[idx..tagStr.IndexOf('"', idx)];
            idx = tagStr.IndexOf("server_time=") + 13;
            ulong serverTime = ulong.Parse(tagStr[idx..tagStr.IndexOf('"', idx)]);
            return new ThreadNiconicoCommentXmlTag(GetDateTimeJstNow(), thread, ticket, serverTime);
        }


        /// <summary>
        /// マシンのロケール設定に関係なく今の日本標準時を返す
        /// </summary>
        private static DateTime GetDateTimeJstNow()
        {
            return DateTime.SpecifyKind(DateTime.UtcNow.AddHours(9), DateTimeKind.Unspecified);
        }
    }
}
