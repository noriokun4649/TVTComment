#pragma once
#include "RawIPCMessage.h"
#include <istream>
namespace TVTComment
{
	//0x1E(Record Separator),0x1F(Unit Separator)文字は伝送データに含めてはならない
	class IPCProtocolStream
	{
	private:
		std::iostream *baseStream;
		bool deleteStream;
	public:
		std::iostream *GetBaseStream() const noexcept;
		IPCProtocolStream(std::iostream *baseStream,bool deleteStream=true);
		RawIPCMessage Read();
		void Write(const RawIPCMessage &msg);
		~IPCProtocolStream() noexcept;
	};
}