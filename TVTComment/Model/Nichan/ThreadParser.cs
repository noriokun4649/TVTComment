using Sgml;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Nichan
{
    class ThreadParserException : NichanException
    {
        public ThreadParserException() : base() { }
        public ThreadParserException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// 2chのスレッドを解析するパーサ
    /// </summary>
    interface IThreadParser
    {
        Thread Parse(XDocument doc);
    }

    /// <summary>
    /// 1行目が&lt;!DOCTYPE html&gt;から始まり、&lt;meta name="viewport" /&gt;が設定されていないスレ用パーサ
    /// </summary>
    class Type1ThreadParser : IThreadParser
    {
        private static DateTime? GetDate(string str)
        {
            return DatParser.GetDate(str);
        }

        public Thread Parse(XDocument doc)
        {
            var ret = new Thread
            {
                Title = HttpUtility.HtmlDecode(doc.XPathSelectElement(@"/html/body/h1[@class=""title""]")?.Value)
            };
            if (ret.Title == null) throw new ThreadParserException();
            foreach (XElement elem in doc.XPathSelectElements(@"/html/body/div[@class=""thread""]/div[@class=""post""]"))
            {
                var res = new Res
                {
                    Number = (int)elem.Attribute("id"),
                    Name = HttpUtility.HtmlDecode(elem.XPathSelectElement(@"div[@class=""name""]/b").Value),
                    Mail = HttpUtility.HtmlDecode(elem.XPathSelectElement(@"div[@class=""name""]/b/a")?.Attribute("href").Value)
                };
                string dateStr = elem.XPathSelectElement(@"div[@class=""date""]").Value;
                int idx = dateStr.IndexOf("ID:");
                res.UserId = idx != -1 ? dateStr[(idx + 3)..] : null;
                res.Date = GetDate(dateStr);
                res.Text = elem.XPathSelectElement(@"div[@class=""message""]");
                ret.Res.Add(res);
            }
            ret.ResCount = ret.Res.Count;
            return ret;
        }
    }

    /// <summary>
    /// 1行目が&lt;html prefix="og: http://ogp.me/ns#"&gt;から始まるスレ用パーサ
    /// </summary>
    class Type2ThreadParser : IThreadParser
    {
        private static DateTime? GetDate(string str)
        {
            return DatParser.GetDate(str);
        }

        public Thread Parse(XDocument doc)
        {
            var ret = new Thread
            {
                Title = HttpUtility.HtmlDecode(doc.XPathSelectElement(@"/html/body/div/span/h1")?.Value)
            };
            if (ret.Title == null) throw new ThreadParserException();

            foreach (XElement elem in doc.XPathSelectElements(@"/html/body/div/span/div/dl[@class=""thread""]/dt"))
            {
                var res = new Res();
                string str = (string)elem.XPathEvaluate(@"string(text()[1])");
                res.Number = int.Parse(str.Substring(0, str.IndexOf(' ')));
                res.Name = HttpUtility.HtmlDecode(elem.XPathSelectElement(@".//b").Value);
                res.Mail = elem.Element("a")?.Attribute("href")?.Value;
                if (res.Mail != null)
                    res.Mail = HttpUtility.HtmlDecode(res.Mail);
                str = (string)elem.XPathEvaluate(@"string(text()[last()])");
                int idx = str.IndexOf("ID:");
                res.UserId = idx == -1 ? null : str[(idx + 3)..];
                res.Date = GetDate(str);
                res.Text = elem.XPathSelectElement(@"following-sibling::dd[1]");
                XElement[] brs = res.Text.Elements("br").ToArray();
                if (brs.Length >= 2)
                {
                    brs[^2].Remove();
                    brs[^1].Remove();
                }
                ret.Res.Add(res);
            }
            ret.ResCount = ret.Res.Count;
            return ret;
        }
    }

    /// <summary>
    /// 1行目が&lt;!DOCTYPE html&gt;から始まり、&lt;meta name="viewport" /&gt;が設定されているスレ用パーサ
    /// </summary>
    class Type3ThreadParser : IThreadParser
    {
        private static DateTime? GetDate(string str)
        {
            return DatParser.GetDate(str);
        }

        public Thread Parse(XDocument doc)
        {
            var ret = new Thread
            {
                Title = HttpUtility.HtmlDecode(doc.XPathSelectElement(@"/html/head/title")?.Value)
            };
            if (ret.Title == null) throw new ThreadParserException();

            foreach (XElement elem in doc.XPathSelectElements(@"/html/body/div/div[@class=""thread""]/div[@class=""post""]"))
            {
                var res = new Res
                {
                    Number = (int)elem.Attribute("id"),
                    Name = HttpUtility.HtmlDecode(elem.XPathSelectElement(@"div[@class=""meta""]/span[@class=""name""]/b").Value),
                    Mail = HttpUtility.HtmlDecode(elem.XPathSelectElement(@"div[@class=""meta""]/span[@class=""name""]/b/a")?.Attribute("href").Value),
                    UserId = elem.Attribute("data-userid").Value
                };
                if (res.UserId.Length > 3) res.UserId = res.UserId[3..];
                string dateStr = elem.XPathSelectElement(@"div[@class=""meta""]/span[@class=""date""]").Value;
                res.Date = GetDate(dateStr);
                res.Text = elem.XPathSelectElement(@"div[@class=""message""]/span");
                ret.Res.Add(res);
            }
            ret.ResCount = ret.Res.Count;
            return ret;
        }
    }

    // 既存の3形式で解析できない現行HTML・項目欠損を補完
    class FallbackThreadParser : IThreadParser
    {
        private static bool HasClass(XElement element, string name) => ((string)element.Attribute("class") ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Contains(name);
        public Thread Parse(XDocument doc)
        {
            var thread = new Thread
            {
                Title = doc.Descendants("h1").FirstOrDefault()?.Value ?? doc.Descendants("title").FirstOrDefault()?.Value
            };
            foreach (var post in doc.Descendants().Where(x => HasClass(x, "post")))
            {
                var numberText = (string)post.Attribute("id") ?? post.Descendants().FirstOrDefault(x => HasClass(x, "number"))?.Value ?? "";
                if (!int.TryParse(Regex.Match(numberText, @"\d+").Value, out var number))
                    continue;
                var body = post.Descendants().FirstOrDefault(x => HasClass(x, "message"));
                if (body == null)
                    continue;
                var date = post.Descendants().FirstOrDefault(x => HasClass(x, "date"))?.Value ?? "";
                var userId = (string)post.Attribute("data-userid") ?? Regex.Match(date, @"ID:([^\s]+)").Groups[1].Value;
                if (userId.StartsWith("ID:", StringComparison.Ordinal))
                    userId = userId.Substring(3);
                var name = post.Descendants().FirstOrDefault(x => HasClass(x, "name"));
                thread.Res.Add(new Res
                {
                    Number = number,
                    Name = name?.Value ?? "",
                    Mail = (string)name?.Descendants("a").FirstOrDefault()?.Attribute("href"),
                    Date = DatParser.GetDate(date),
                    UserId = userId,
                    Text = new XElement(body)
                });
            }
            if (thread.Res.Count == 0)
            {
                foreach (var dt in doc.Descendants("dt"))
                {
                    if (!int.TryParse(Regex.Match(dt.Value, @"^\s*(\d+)").Groups[1].Value, out var number))
                        continue;
                    var body = dt.ElementsAfterSelf().FirstOrDefault();
                    if (body?.Name.LocalName != "dd")
                        continue;
                    thread.Res.Add(new Res
                    {
                        Number = number,
                        Name = dt.Descendants("b").FirstOrDefault()?.Value ?? "",
                        Mail = (string)dt.Descendants("a").FirstOrDefault()?.Attribute("href"),
                        Date = DatParser.GetDate(dt.Value),
                        UserId = Regex.Match(dt.Value, @"ID:([^\s]+)").Groups[1].Value,
                        Text = new XElement(body)
                    });
                }
            }
            if (thread.Title == null || thread.Res.Count == 0)
                throw new ThreadParserException();
            thread.ResCount = thread.Res.Count;
            return thread;
        }
    }

    public static class ThreadParser
    {
        private static IThreadParser GetThreadParser(XDocument doc)
        {
            if (doc.Element("html")?.Attribute("prefix") == null)
            {
                if (doc.XPathSelectElement(@"/html/head/meta[@name=""viewport""]") == null)
                    return new Type1ThreadParser();
                else
                    return new Type3ThreadParser();
            }
            else
                return new Type2ThreadParser();
        }

        /// <summary>
        /// スレッドのHTMLを格納した<see cref="TextReader"/>からスレッドを解析。
        /// ただし<see cref="Thread.Uri"/>および<see cref="Thread.Name"/>はnull。
        /// </summary>
        /// <exception cref="ThreadParserException">解析エラーの場合</exception>
        public static Thread ParseFromStream(TextReader reader)
        {
            using var sgmlReader = new SgmlReader { DocType = "HTML", IgnoreDtd = true, InputStream = reader };
            sgmlReader.WhitespaceHandling = WhitespaceHandling.None;
            sgmlReader.CaseFolding = CaseFolding.ToLower;
            var doc = XDocument.Load(sgmlReader);

            try
            {
                Thread ret = GetThreadParser(doc).Parse(doc);
                if (ret.Res.Count > 0 && ret.Res.All(r => r.Text != null))
                    return ret;
            }
            catch (Exception e) when (e is ThreadParserException || e is NullReferenceException || e is FormatException || e is ArgumentException || e is OverflowException)
            { }
            return new FallbackThreadParser().Parse(doc);
        }
    }
}
