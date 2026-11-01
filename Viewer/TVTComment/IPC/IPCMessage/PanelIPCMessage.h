#pragma once
#include "IIPCMessage.h"

namespace TVTComment
{
	class PanelIPCMessage : public IIPCMessage
	{
	public:
		std::string Operation;
		std::vector<std::string> Arguments;

		std::string GetMessageName() const override;
		std::vector<std::string> Encode() const override;
		void Decode(const std::vector<std::string> &contents) override;
	};
}
