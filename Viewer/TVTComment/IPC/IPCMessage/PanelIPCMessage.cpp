#include "stdafx.h"
#include "PanelIPCMessage.h"
#include "../IPCMessageDecodeError.h"

namespace TVTComment
{
	std::string PanelIPCMessage::GetMessageName() const
	{
		return "Panel";
	}

	std::vector<std::string> PanelIPCMessage::Encode() const
	{
		std::vector<std::string> result{ Operation };
		result.insert(result.end(), Arguments.begin(), Arguments.end());
		for (auto &field : result)
			for (auto &character : field)
				if (character == '\x1e' || character == '\x1f' || character == '\0') character = ' ';
		return result;
	}

	void PanelIPCMessage::Decode(const std::vector<std::string> &contents)
	{
		if (contents.empty() || contents.front().empty())
			throw IPCMessageDecodeError("Panel operation is missing");
		Operation = contents.front();
		Arguments.assign(contents.begin() + 1, contents.end());
	}
}
