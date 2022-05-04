using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Nichan
{
    class BoardSubjectParserException : NichanException
    {
        public BoardSubjectParserException() : base()
        {
        }

        public BoardSubjectParserException(Exception inner) : base(null, inner)
        {
        }
    }

    class SubjecttxtParser
    {
        /// <summary>
        /// subject.txtを格納した<see cref="TextReader"/>からスレッドのリストを解析。
        /// ただしスレッドのURIはnull。
        /// </summary>
        /// <exception cref="BoardSubjectParserException">解析エラーの場合</exception>
        public static async IAsyncEnumerable<Thread> ParseFromStream(TextReader reader)
        {
            while (true)
            {
                string line = await reader.ReadLineAsync();
                if (line == null) break;

                int sepIdx = line.IndexOf("<>");
                if (sepIdx == -1)
                    throw new BoardSubjectParserException();
                string dat = line[..sepIdx];
                string title = line[(sepIdx + 2)..];

                if (!dat.EndsWith(".dat"))
                {
                    throw new BoardSubjectParserException();
                }
                dat = dat[..^4];

                int start = title.LastIndexOf("(");
                int end = title.LastIndexOf(")");
                if (start == -1 || end == -1 || start >= end)
                    throw new BoardSubjectParserException();

                int resCount;
                try
                {
                    resCount = int.Parse(title[(start + 1)..end]);
                }
                catch (Exception e) when (e is FormatException || e is OverflowException)
                {
                    throw new BoardSubjectParserException(e);
                }

                title = title[..start].Trim();

                yield return new Thread() { Name = dat, Title = title, ResCount = resCount };
            }
        }
    }
}
