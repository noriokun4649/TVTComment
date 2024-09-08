#include "stdafx.h"
#include "RawIPCMessage.h"

namespace TVTComment
{
	std::string RawIPCMessage::ToString() const
	{
		std::string ret = this->MessageName;
		for (const std::string &content : this->Contents)
			ret += " " + content;
		return ret;
	}
}