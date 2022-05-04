using CoreTweet;
using CoreTweet.V2;
using System.Drawing;

namespace TVTComment.Model.TwitterUtils
{
    class ChatTwitterStatusToChat
    {
        public static Chat Convert(Status status)
        {
            return new Chat(status.CreatedAt.DateTime.ToLocalTime(), status.FullText ?? status.Text, Chat.PositionType.Normal, Chat.SizeType.Normal, Color.FromArgb(0, 172, 238), status.User.ScreenName, (int)status.Id);
        }
        public static Chat Convert(FilterStreamResponse status)
        {
            var data = status.Data;
            var user = status.Includes.Users[0];
            return new Chat(data.CreatedAt.Value.LocalDateTime , data.Text, Chat.PositionType.Normal, Chat.SizeType.Normal, Color.FromArgb(0, 172, 238), user.Username , (int)data.Id);
        }
    }
}
