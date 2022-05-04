#pragma once
#include <string>

namespace TVTComment
{
	struct ChannelInfo
	{
		enum class TuningSpaceType
		{
			Unknown,Terrestrial,BS,CS
		};

		int SpaceIdx;

		int ChannelIdx;

		TuningSpaceType TuningSpace;

		int RemoteControlKeyID;

		unsigned short NetworkID;

		unsigned short TransportStreamID;

		std::string NetworkName;

		std::string TransportStreamName;

		std::string ChannelName;

		unsigned short ServiceID;

		bool Hidden;//TVTestの設定で非表示になっている
	};
}