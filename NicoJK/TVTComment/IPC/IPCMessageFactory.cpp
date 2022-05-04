#include "stdafx.h"
#include "IPCMessageFactory.h"
#include "IPCMessageDecodeError.h"
#include "IPCMessage/ChatIPCMessage.h"
#include "IPCMessage/ChannelListIPCMessage.h"
#include "IPCMessage/CurrentChannelIPCMessage.h"
#include "IPCMessage/ChannelSelectIPCMessage.h"
#include "IPCMessage/TimeIPCMessage.h"
#include "IPCMessage/CloseIPCMessage.h"
#include "IPCMessage/SetChatOpacityIPCMessage.h"
#include "IPCMessage/CommandIPCMessage.h"

namespace TVTComment
{
	std::unique_ptr<IIPCMessage> MakeIPCMessageFromRaw(const RawIPCMessage &rawmsg)
	{
		IIPCMessage *msg;
		if (rawmsg.MessageName == "Chat")
			msg = new ChatIPCMessage();
		else if (rawmsg.MessageName == "ChannelList")
			msg = new ChannelListIPCMessage();
		else if (rawmsg.MessageName == "CurrentChannel")
			msg = new CurrentChannelIPCMessage();
		else if (rawmsg.MessageName == "ChannelSelect")
			msg = new ChannelSelectIPCMessage();
		else if (rawmsg.MessageName == "Time")
			msg = new TimeIPCMessage();
		else if (rawmsg.MessageName == "Close")
			msg = new CloseIPCMessage();
		else if (rawmsg.MessageName == "SetChatOpacity")
			msg = new SetChatOpacityIPCMessage();
		else if (rawmsg.MessageName == "Command")
			msg = new CommandIPCMessage();
		else
			throw IPCMessageDecodeError("不明なMessageNameです: " + rawmsg.ToString());

		std::unique_ptr<IIPCMessage> ret(msg);
		ret->Decode(rawmsg.Contents);
		return ret;
	}
}