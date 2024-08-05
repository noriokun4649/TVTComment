using Dwango.Nicolive.Chat.Data;
using Dwango.Nicolive.Chat.Service.Edge;
using Google.Protobuf.WellKnownTypes;
using System;
using System.Collections.Generic;
using static Dwango.Nicolive.Chat.Data.Chat;
using static Dwango.Nicolive.Chat.Data.Chat.Types;
using static Dwango.Nicolive.Chat.Data.NicoliveMessage;
using static Dwango.Nicolive.Chat.Service.Edge.ChunkedMessage;
using static System.Net.Mime.MediaTypeNames;

namespace TVTComment.Model.NiconicoUtils
{
    class NiconicoCommentProtobufParser
    {
        private string postId;

        public NiconicoCommentProtobufParser(string postId)
        { 
            this.postId = postId;
        }

        private readonly Queue<NiconicoCommentXmlTag> chats = new ();
        
        public void Push(ChunkedMessage msg)
        {            
            if (msg.PayloadCase == PayloadOneofCase.Message && msg.Message.DataCase == DataOneofCase.Chat)
            {
                chats.Enqueue(GetChatTag(msg));
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

        private ChatNiconicoCommentXmlTag GetChatTag(ChunkedMessage msg)
        {
            
            if (msg.State?.ProgramStatus.State == ProgramStatus.Types.State.Ended) //放送のAlertで切断メッセージが来たらException
                throw new ConnectionDisconnectNicoLiveCommentReceiverException();

            var chat = msg.Message.Chat;
            string userId;
            if (chat.HasRawUserId)
            {
                userId = $"{chat.RawUserId} ({chat.Name})";
            }
            else
            {
                userId = chat.HashedUserId;
            }

            var text = chat.Content;
            var thread = msg.Meta.Origin.Chat.LiveId.ToString();
            var no = chat.No;
            var vpos = chat.Vpos;
            var mail = GetCommands(chat);
            var premium = chat.AccountStatus == AccountStatus.Premium ? 1 : 0;
            var anonymity = chat.SourceCase == SourceOneofCase.HashedUserId ? 1 : 0;
            var myPost = postId == chat.HashedUserId;
            GetTimeStamp(msg.Meta.At,out var date, out var dateUsec);
            return new ChatNiconicoCommentXmlTag(text,thread,no,vpos,date,dateUsec,mail,userId,premium,anonymity,0, myPost);
        }

        private void GetTimeStamp(Timestamp timestamp,out long data, out int dataUsec)
        {
            data = timestamp.Seconds;
            dataUsec = timestamp.Nanos / 1000;
        }

        private string GetCommands(Dwango.Nicolive.Chat.Data.Chat chat)
        {
            try
            {
                var command = chat.Modifier;
                var mail = "";
                if (chat.SourceCase == SourceOneofCase.HashedUserId)
                {
                    mail += "184 ";
                }

                mail += command.Opacity.ToString().ToLower() + " ";
                mail += command.Position.ToString().ToLower() + " ";
                mail += command.Size.ToString().ToLower() + " ";
                mail += command.Font.ToString().ToLower() + " ";

                if (command.HasNamedColor) {
                    mail += command.NamedColor.ToString().ToLower();
                } else if (command.FullColor != null)
                {
                    mail += "#";
                    mail += command.FullColor.R.ToString("X2");
                    mail += command.FullColor.G.ToString("X2");
                    mail += command.FullColor.B.ToString("X2");
                }
                return mail;
            } catch (Exception ex)
            {
                throw new InvalidPlayerStatusNicoLiveCommentReceiverException(ex.Message);
            }
        }
    }
}
