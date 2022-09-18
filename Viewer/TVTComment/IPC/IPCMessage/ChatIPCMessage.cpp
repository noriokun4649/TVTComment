#include "stdafx.h"
#include "ChatIPCMessage.h"
#include "../IPCMessageDecodeError.h"
#include <cstdlib>

namespace TVTComment
{
	std::string ChatIPCMessage::GetMessageName() const
	{
		return u8"Chat";
	}

	std::vector<std::string> ChatIPCMessage::Encode() const
	{
		std::vector<std::string> ret(4);
		ret.push_back(this->Chat.text);
		switch (this->Chat.position)
		{
		case Chat::Position::Default:
			ret.push_back("Default");
			break;
		case Chat::Position::Bottom:
			ret.push_back("Bottom");
			break;
		case Chat::Position::Top:
			ret.push_back("Top");
			break;
		}
		switch (this->Chat.size)
		{
		case Chat::Size::Default:
			ret.push_back("Default");
			break;
		case Chat::Size::Small:
			ret.push_back("Small");
			break;
		case Chat::Size::Large:
			ret.push_back("Large");
			break;
		}
		ret.push_back(std::to_string(this->Chat.color.R) + "," + std::to_string(this->Chat.color.G) + "," + std::to_string(this->Chat.color.B));

		if (this->Chat.self)
			ret.push_back("true");
		else if (!this->Chat.self)
			ret.push_back("false");

		return ret;
	}
	
	void ChatIPCMessage::Decode(const std::vector<std::string> &contents)
	{
		if (contents.size() != 5)
			throw IPCMessageDecodeError("Chatのcontentsの数が4以外です");

		this->Chat.text = contents[0];

		if (contents[1] == "Default")
			this->Chat.position = Chat::Position::Default;
		else if (contents[1] == "Bottom")
			this->Chat.position = Chat::Position::Bottom;
		else if (contents[1] == "Top")
			this->Chat.position = Chat::Position::Top;
		else
			throw IPCMessageDecodeError("ChatのPositionの値が不正です: "+contents[1]);

		if (contents[2] == "Default")
			this->Chat.size = Chat::Size::Default;
		else if (contents[2] == "Small")
			this->Chat.size = Chat::Size::Small;
		else if (contents[2] == "Large")
			this->Chat.size = Chat::Size::Large;
		else
			throw IPCMessageDecodeError("ChatのSizeの値が不正です: " + contents[1]);

		std::string colorstr = contents[3]+",";
		int count=0;
		for (std::string::size_type i = 0, j = colorstr.find(','); j != std::string::npos; i = j + 1, j = colorstr.find(',', i),count++)
		{
			std::size_t startIdx = i;
			switch (count)
			{
			case 0:
				this->Chat.color.R = (unsigned char)std::strtoul(colorstr.c_str()+startIdx,nullptr,0);
				break;
			case 1:
				this->Chat.color.G = (unsigned char)std::strtoul(colorstr.c_str() + startIdx, nullptr, 0);
				break;
			case 2:
				this->Chat.color.B = (unsigned char)std::strtoul(colorstr.c_str() + startIdx, nullptr, 0);
				break;
			}
		}
		//フォーマットがR,G,BでもR,G,B,でもないなら
		if (count != 3 && count != 4)
			throw IPCMessageDecodeError("ChatのColorのフォーマットが不正です: " + contents[3]);

		if (contents[4] == "true")
			this->Chat.self = true;
		else if(contents[4] == "false")
			this->Chat.self = false;
		else
			throw IPCMessageDecodeError("ChatのSelfの値が不正です: " + contents[4]);
	}
}