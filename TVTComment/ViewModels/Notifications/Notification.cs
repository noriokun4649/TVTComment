using System;
using System.Linq;

namespace TVTComment.ViewModels.Notifications
{
    sealed class Notification
    {
        public string Title { get; set; }
        public object Content { get; set; }

        public string Details => Content is string text && (text.Length > 600 || text.Count(c => c == '\n') >= 8) ? text : null;

        public object DisplayContent
        {
            get
            {
                string details = Details;
                if (details == null) return Content;

                string text = details.Replace("\r\n", "\n");
                int end = text.IndexOf("\n\n", StringComparison.Ordinal);
                if (end < 0) end = text.IndexOf('\n');
                if (end < 0) end = text.Length;
                return end <= 240 ? text.Substring(0, end) : text.Substring(0, 240) + "…";
            }
        }
    }
}
