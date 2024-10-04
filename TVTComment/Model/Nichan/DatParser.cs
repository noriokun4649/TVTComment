// 参考: http://info.5ch.net/index.php/Monazilla/develop/dat

using Sgml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;


namespace Nichan
{
    public class DatParserException : NichanException
    {
        public DatParserException() { }
        public DatParserException(string message) : base(message) { }
        public DatParserException(string message, Exception inner) : base(message, inner) { }
    }

    public class DatParser
    {
        public bool FromTheMiddle { get; }
        /// <summary>
        /// 解析結果のスレタイが入る。解析前の場合は<c>null</c>。
        /// </summary>
        public string ThreadTitle { get; private set; }

        public DatParser() : this(false)
        {
        }

        /// <param name="fromTheMiddle">
        /// 真の場合、datデータを最初からでなく中途半端な位置から与えられても解析する。その場合スレタイの解析は行われないしレス番号は正しくない。
        /// </param>
        public DatParser(bool fromTheMiddle)
        {
            FromTheMiddle = fromTheMiddle;
            Reset();
        }

        /// <summary>
        /// DAT文字列を追加し解析する
        /// </summary>
        /// <exception cref="DatParserException"></exception>
        public void Feed(string datString)
        {
            buffer += datString;
            if (buffer == "")
                return;
            var rows = buffer.Split('\n');
            var reses = rows[..^1].Select(x => x.Split("<>")).ToArray();
            buffer = rows[^1];

            bool isFirstLine = isBeforeFirstLine && reses.Length > 0; // 真ならreses[0]は最初の行
            if (isFirstLine)
            {
                isBeforeFirstLine = false;
            }

            if (isFirstLine && FromTheMiddle)
            {
                // 中途半端な位置から始まるデータの場合、最初の行は省く
                reses = reses[1..];
            }

            // 6フィールドでも実害がなさそうなのでエラー出さずに無視する
            // 運営スレが6フィールドになる模様？
            // 通常は5フィールドだが、なぜか先頭行が6フィールドあるdatが確認されている
            /*
            if (!reses.All(x => x.Length >= 5))
            {
                //throw new DatParserException();
            }
            */

            if (isFirstLine && !FromTheMiddle)
            {
                // 最初の行からスレタイを取得
                ThreadTitle = reses[0][4];
            }

            using var sgmlReader = new SgmlReader() { DocType = "HTML", IgnoreDtd = false };
            sgmlReader.WhitespaceHandling = WhitespaceHandling.All;
            sgmlReader.CaseFolding = CaseFolding.ToLower;

            foreach (var row in reses)
            {
                int startIdx = row[2].IndexOf("ID:");
                string userId = startIdx != -1 ? row[2][(startIdx + 3)..] : null;

                using var reader = new StringReader($"<html><div>{row[3]}</div></html>");
                sgmlReader.InputStream = reader;
                XElement text = XDocument.Load(sgmlReader).XPathSelectElement("./html/div");

                this.reses.Enqueue(new Res
                {
                    Number = ++resNum,
                    Name = HttpUtility.HtmlDecode(row[0]),
                    Mail = HttpUtility.HtmlDecode(row[1]),
                    UserId = userId,
                    Date = GetDate(row[2]),
                    Text = text,
                });
            }
        }

        /// <summary>
        /// 解析結果のレスのキューからレスをポップして返す
        /// </summary>
        public Res PopRes()
        {
            return reses.TryDequeue(out Res res) ? res : null;
        }

        /// <summary>
        /// パーサの状態を初期化する
        /// </summary>
        public void Reset()
        {
            ThreadTitle = null;
            buffer = "";
            reses.Clear();
            resNum = 0;
            isBeforeFirstLine = true;
        }

        private string buffer;
        private readonly Queue<Res> reses = new Queue<Res>();
        private int resNum;
        private bool isBeforeFirstLine;

        private static readonly Regex reDate = new Regex(@"(\d+)/(\d+)/(\d+)[^ ]* (\d+):(\d+):(\d+)(\.(?<sec>\d+)| )");
        private static DateTime? GetDate(string str)
        {
            Match m = reDate.Match(str);
            if (m.Success) {
                var sec = 0;
                if (m.Groups["sec"].Success)
                {
                    sec = int.Parse(m.Groups["sec"].Value);
                    if (sec >= 10 && sec <= 99) sec *= 10;
                }
                return new DateTime(
                    int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                    int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), sec, DateTimeKind.Local
                );
            }
            else
                return null;
        }
    }
}
