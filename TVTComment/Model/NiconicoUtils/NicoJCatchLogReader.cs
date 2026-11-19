using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace TVTComment.Model.NiconicoUtils
{
    // NicojCatch標準形式のjk番号フォルダ・UNIX時刻名UTF-8ログ
    internal static class NicoJCatchLogReader
    {
        internal sealed record Comment(string Identity, DateTime Time, ChatNiconicoCommentXmlTag Tag)
        {
            public Chat ToChat()
            {
                var chat = ChatNiconicoCommentXmlTagToChat.Convert(Tag);
                return chat.Time == Time ? chat : new Chat(Time, chat.Text, chat.Position, chat.Size, chat.Color, chat.UserId, chat.Number);
            }
        }
        internal sealed record Result(Queue<Comment> Comments, string[] Files);

        public static async Task<Result> ReadAsync(string folder, int jkId, DateTime from, DateTime until, CancellationToken token)
        {
            long first = new DateTimeOffset(from, TimeSpan.FromHours(9)).ToUnixTimeSeconds();
            long last = new DateTimeOffset(until, TimeSpan.FromHours(9)).ToUnixTimeSeconds();
            var files = new List<string>();
            string preceding = null;
            foreach (string path in Directory.EnumerateFiles(Path.Combine(folder, $"jk{jkId}"), "*.txt"))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.Length != 10 || !long.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out long start) || start > last)
                    continue;
                if (start > first) files.Add(path);
                else if (string.CompareOrdinal(path, preceding) > 0) preceding = path;
            }
            if (preceding != null) files.Add(preceding);
            files.Sort(StringComparer.Ordinal);
            var comments = new Dictionary<string, Comment>();
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    65536, FileOptions.Asynchronous);
                SeekNearTime(stream, first - 10, token);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                if (stream.Position != 0)
                    await reader.ReadLineAsync(token).ConfigureAwait(false);
                string line;
                while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
                {
                    token.ThrowIfCancellationRequested();
                    var tag = ParseChat(line);
                    if (tag == null || tag.Date < first)
                        continue;
                    if (tag.Date > last + 10)
                        break;
                    if (tag.Date > last) continue;
                    var time = DateTimeOffset.FromUnixTimeSeconds(tag.Date).ToOffset(TimeSpan.FromHours(9)).DateTime
                        .AddTicks(Math.Clamp(tag.DateUsec, 0, 999999) * 10L);
                    if (from < time && time <= until)
                    {
                        string id = tag.Thread.Length != 0 && tag.No > 0 ? tag.Thread + "\0" + tag.No.ToString(CultureInfo.InvariantCulture) : line;
                        comments.TryAdd(id, new Comment(id, time, tag));
                    }
                }
            }
            return new Result(new Queue<Comment>(comments.Values.OrderBy(x => x.Time)), files.Select(Path.GetFileName).ToArray());
        }

        private static ChatNiconicoCommentXmlTag ParseChat(string line)
        {
            if (!line.StartsWith("<chat", StringComparison.Ordinal)) return null;
            XElement chat;
            try { chat = XElement.Parse(line, LoadOptions.PreserveWhitespace); }
            catch (XmlException) { return null; }
            int Number(string name) => int.TryParse((string)chat.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
            if (chat.Name != "chat" || !long.TryParse((string)chat.Attribute("date"), NumberStyles.None, CultureInfo.InvariantCulture, out long date)
                || Number("abone") != 0 || Number("deleted") != 0) return null;
            return new ChatNiconicoCommentXmlTag(chat.Value, (string)chat.Attribute("thread") ?? "", Number("no"), 0, date,
                Number("date_usec"), (string)chat.Attribute("mail") ?? "", (string)chat.Attribute("user_id") ?? "", 0, 0, 0, false);
        }

        // 時系列順ログの二分探索による全件読み込みの回避
        private static void SeekNearTime(FileStream stream, long date, CancellationToken token)
        {
            long low = 0, high = stream.Length;
            while (high - low > 65536)
            {
                token.ThrowIfCancellationRequested();
                long middle = low + (high - low) / 2;
                stream.Position = middle;
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                reader.ReadLine();
                string line;
                ChatNiconicoCommentXmlTag tag = null;
                while ((line = reader.ReadLine()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    tag = ParseChat(line);
                    if (tag != null)
                        break;
                }
                if (tag?.Date < date)
                    low = middle;
                else
                    high = middle;
            }
            stream.Position = low;
        }
    }
}
