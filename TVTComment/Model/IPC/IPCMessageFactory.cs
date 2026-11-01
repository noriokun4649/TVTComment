namespace TVTComment.Model.IPC
{
    static class IPCMessageFactory
    {
        public static IPCMessage.IIPCMessage FromRawIPCMessage(RawIPCMessage rawmsg)
        {
            IPCMessage.IIPCMessage ret = rawmsg.MessageName switch
            {
                "Chat" => new IPCMessage.ChatIPCMessage(),
                "ChannelList" => new IPCMessage.ChannelListIPCMessage(),
                "ChannelSelect" => new IPCMessage.ChannelSelectIPCMessage(),
                "CurrentChannel" => new IPCMessage.CurrentChannelIPCMessage(),
                "Time" => new IPCMessage.TimeIPCMessage(),
                "Close" => new IPCMessage.CloseIPCMessage(),
                "SetChatOpacity" => new IPCMessage.SetChatOpacityIPCMessage(),
                "Command" => new IPCMessage.CommandIPCMessage(),
                "Panel" => new IPCMessage.PanelIPCMessage(),
                _ => throw new IPCMessage.IPCMessageDecodeException("不明なMessageNameです: " + rawmsg.ToString()),
            };
            ret.Decode(rawmsg.Contents);
            return ret;
        }
    }
}
