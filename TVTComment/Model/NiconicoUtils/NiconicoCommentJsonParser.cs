using System.Collections.Generic;
using System.Text.Json;

namespace TVTComment.Model.NiconicoUtils
{
    class NiconicoCommentJsonParser
    {
        private string MyUserId;

        public NiconicoCommentJsonParser(string myUserId)
        {
            MyUserId = myUserId;
        }

        private readonly Queue<NiconicoCommentXmlTag> chats = new Queue<NiconicoCommentXmlTag>();
        
        public void Push(string str)
        {
            var jsons = JsonDocument.Parse(str).RootElement;
            if (jsons.TryGetProperty("chat", out var chat))
            {
                chats.Enqueue(GetChatTag(chat));
            }
        }

        public NiconicoCommentXmlTag Pop()
        {
            return chats.Dequeue();
        }

        public bool DataAvailable()
        {
            return chats.Count > 0;
        }

        private ChatNiconicoCommentXmlTag GetChatTag(JsonElement chat)
        {
            string text = chat.GetProperty("content").ToString();
            string thread = chat.GetProperty("thread").ToString();
            int no = chat.GetProperty("no").GetInt32();
            int vpos = chat.TryGetProperty("vpos", out var pos) ? pos.GetInt32() : 0;
            long date = chat.GetProperty("date").GetInt64();
            int dateUsec = chat.GetProperty("date_usec").GetInt32();
            string mail = chat.TryGetProperty("mail", out var str) ? str.GetString() : "";
            string userId = chat.GetProperty("user_id").ToString();
            int premium = chat.TryGetProperty("premium", out var pre) ? pre.GetInt32() : 0 ;
            int anonymity = chat.TryGetProperty("anonymity", out var ano) ? ano.GetInt32() : 0;
            if (premium == 2 && text.Equals("/disconnect")) //放送のAlertで切断メッセージが来たらException
                throw new ConnectionDisconnectNicoLiveCommentReceiverException();
            return new ChatNiconicoCommentXmlTag(text,thread,no,vpos,date,dateUsec,mail,userId,premium,anonymity,0,userId.Equals(MyUserId));
        }
    }
}
