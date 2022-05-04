#include "stdafx.h"
#include <stdexcept>
#include "SetChatOpacityIPCMessage.h"
#include "../IPCMessageDecodeError.h"

namespace TVTComment
{
	std::string SetChatOpacityIPCMessage::GetMessageName() const
	{
		return u8"SetChatOpacity";
	}
	std::vector<std::string> SetChatOpacityIPCMessage::Encode() const
	{
		return{ std::to_string(Opacity) };
	}
	void SetChatOpacityIPCMessage::Decode(const std::vector<std::string> &contents)
	{
		if (contents.size() != 1)
			throw IPCMessageDecodeError("SetChatOpacityのcontentsの数が1以外です");
		int val;
		try
		{
			val = std::stoi(contents[0]);
		}
		catch (std::invalid_argument)
		{
			throw IPCMessageDecodeError("SetChatOpacityのフォーマットが不正です");
		}
		if (0 <= val && val <= 255)
			this->Opacity = (unsigned char)val;
		else
			throw IPCMessageDecodeError("SetChatOpacityの透過度が0～255の範囲外です");
	}
}