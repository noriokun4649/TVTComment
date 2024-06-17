using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace TVTComment.Model.NiconicoUtils
{
    static class ChatNiconicoCommentXmlTagToChat
    {
        private static readonly Dictionary<string, Color> colorNameMapping = new Dictionary<string, Color> {
            {"red" , Color.FromArgb(0xFF, 0x00, 0x00)},
            {"pink", Color.FromArgb(0xFF, 0x80, 0x80)},
            {"orange",Color.FromArgb(0xFF, 0xC0, 0x00)},
            {"yellow",Color.FromArgb(0xFF, 0xFF, 0x00)},
            {"green", Color.FromArgb(0x00, 0xFF, 0x00) },
            {"cyan", Color.FromArgb(0x00, 0xFF, 0xFF) },
            {"blue", Color.FromArgb(0x00, 0x00, 0xFF) },
            {"purple", Color.FromArgb(0xC0, 0x00, 0xFF)},
            {"black", Color.FromArgb(0x00, 0x00, 0x00) },
            {"white2", Color.FromArgb(0xCC, 0xCC, 0x99) },
            {"niconicowhite", Color.FromArgb(0xCC, 0xCC, 0x99) },
            {"red2", Color.FromArgb(0xCC, 0x00, 0x33) },
            {"truered", Color.FromArgb(0xCC, 0x00, 0x33) },
            {"pink2",Color.FromArgb(0xFF, 0x33, 0xCC)},
            {"orange2", Color.FromArgb(0xFF, 0x66, 0x00) },
            {"passionorange", Color.FromArgb(0xFF, 0x66, 0x00) },
            {"yellow2", Color.FromArgb(0x99, 0x99, 0x00) },
            {"madyellow", Color.FromArgb(0x99, 0x99, 0x00) },
            {"green2", Color.FromArgb(0x00, 0xCC, 0x66) },
            {"elementalgreen", Color.FromArgb(0x00, 0xCC, 0x66) },
            {"cyan2", Color.FromArgb(0x00, 0xCC, 0xCC) },
            {"blue2", Color.FromArgb(0x33, 0x99, 0xFF) },
            {"marineblue", Color.FromArgb(0x33, 0x99, 0xFF) },
            {"purple2", Color.FromArgb(0x66, 0x33, 0xCC)},
            {"nobleviolet", Color.FromArgb(0x66, 0x33, 0xCC) },
            {"black2", Color.FromArgb(0x66, 0x66, 0x66) },
        };

        private static readonly Regex reColor = new Regex("(?:^| )#([0-9A-Fa-f]{6})(?: |$)");

        public static Chat Convert(ChatNiconicoCommentXmlTag tag)
        {
            Chat.PositionType position = Chat.PositionType.Normal;
            Chat.SizeType size = Chat.SizeType.Normal;
            Color color = Color.FromArgb(255, 255, 255);

            var time = DateTimeOffset.FromUnixTimeSeconds(tag.Date).DateTime.ToLocalTime();

            string mail = tag.Mail;

            if (mail.Contains("shita"))
                position = Chat.PositionType.Bottom;
            else if (mail.Contains("ue"))
                position = Chat.PositionType.Top;

            if (mail.Contains("small"))
                size = Chat.SizeType.Small;
            else if (mail.Contains("big"))
                size = Chat.SizeType.Large;

            var match = reColor.Match(mail);
            if (match.Success)
            {
                int colorNum = int.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                color = Color.FromArgb((colorNum >> 16) & 0xFF, (colorNum >> 8) & 0xFF, colorNum & 0xFF);
            }
            else
            {
                var namedColor = colorNameMapping.Where(kv => mail.Contains(kv.Key)).Select(kv => (KeyValuePair<string, Color>?)kv).FirstOrDefault();
                if (namedColor != null)
                    color = namedColor.Value.Value;
            }

            return new Chat(time, tag.Text, position, size, color, tag.UserId, tag.No, tag.SelfComment);
        }
    }
}
