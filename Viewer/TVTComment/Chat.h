#pragma once

#include <string>
#include "Color.h"

namespace TVTComment
{
	class Chat
	{
	public:
		enum class Position {Default,Top,Bottom};
		enum class Size {Default,Small,Large};

		std::string text;
		Size size = Size::Default;
		Position position=Position::Default;
		Color color;
		bool self;
		
		Chat() = default;
		Chat(const std::string &text, const Color &color, Position position=Position::Default,Size size=Size::Default,bool self=false);
	};
}