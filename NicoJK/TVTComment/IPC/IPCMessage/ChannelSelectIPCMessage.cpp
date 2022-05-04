#include "stdafx.h"
#include "ChannelSelectIPCMessage.h"
#include <stdexcept>
#include "../IPCMessageDecodeError.h"

namespace TVTComment
{
	std::string ChannelSelectIPCMessage::GetMessageName() const
	{
		return u8"ChannelSelect";
	}

	std::vector<std::string> ChannelSelectIPCMessage::Encode() const
	{
		throw std::logic_error("ChannelSelectIPCMessage::Encode is not implemented");
	}

	void ChannelSelectIPCMessage::Decode(const std::vector<std::string> &contents)
	{
		if (contents.size() != 3)
			throw IPCMessageDecodeError("ChannelSelectのcontentsの数が3以外です");

		try
		{
			this->SpaceIndex = std::stoi(contents[0]);
			this->ChannelIndex = std::stoi(contents[1]);
			this->ServiceId = (uint16_t)std::stoul(contents[2]);
		}
		catch (std::invalid_argument)
		{
			throw IPCMessageDecodeError("ChannelSelectのフォーマットが不正です");
		}
	}
}