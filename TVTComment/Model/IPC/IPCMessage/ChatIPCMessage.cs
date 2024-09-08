using System;
using System.Collections.Generic;

namespace TVTComment.Model.IPC.IPCMessage
{
    class ChatIPCMessage : IIPCMessage
    {
        public Chat Chat { get; set; }

        public string MessageName => "Chat";

        public void Decode(IEnumerable<string> content)
        {
            throw new NotImplementedException("ChatIPCMessage::Decode is not implemented");
        }

        public IEnumerable<string> Encode()
        {
            string[] ret = new string[5];
            ret[0] = Chat.Text.Replace('\u001E', '〓').Replace('\u001F', '〓');
            switch (Chat.Position)
            {
                case Chat.PositionType.Normal:
                    ret[1] = "Default";
                    break;
                case Chat.PositionType.Top:
                    ret[1] = "Top";
                    break;
                case Chat.PositionType.Bottom:
                    ret[1] = "Bottom";
                    break;
            }
            switch (Chat.Size)
            {
                case Chat.SizeType.Normal:
                    ret[2] = "Default";
                    break;
                case Chat.SizeType.Small:
                    ret[2] = "Small";
                    break;
                case Chat.SizeType.Large:
                    ret[2] = "Large";
                    break;
            }
            ret[3] = $"{Chat.Color.R},{Chat.Color.G},{Chat.Color.B}";

            if (Chat.IsSelfComment)
            {
                ret[4] = "true";
            }
            else
            {
                ret[4] = "false";
            }
            return ret;
        }
    }
}
