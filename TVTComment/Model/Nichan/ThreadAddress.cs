using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nichan
{
    public sealed class ThreadAddress
    {
        public Uri BoardUri { get; }
        public string Board => BoardUri.AbsolutePath.Trim('/');
        public string ThreadId { get; }
        public bool IsAnnouncement => Is5chHost(BoardUri.Host) && ThreadId.Length == 10 && ThreadId.StartsWith("924", StringComparison.Ordinal);
        public Uri ThreadUri => new Uri(BoardUri, $"/test/read.cgi/{Board}/{ThreadId}/");
        public Uri DatUri => new Uri(BoardUri, $"dat/{ThreadId}.dat");
        public Uri ArchiveDatUri => new Uri(BoardUri, $"oyster/{ThreadId.Substring(0, 4)}/{ThreadId}.dat");
        public string Identity => $"{(Is5chHost(BoardUri.Host) ? "5ch" : BoardUri.Authority)}/{Board}/{ThreadId}";

        public ThreadAddress(Uri boardUri, string threadId)
        {
            BoardUri = NormalizeBoard(boardUri);
            if (!Regex.IsMatch(threadId ?? "", "^[0-9]{9,12}$"))
                throw new FormatException("スレッド番号が不正です。");
            ThreadId = threadId;
        }
        public static bool Is5chHost(string host) => new[] { "5ch.io", "5ch.net", "2ch.net" }
            .Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

        public static Uri Normalize(Uri uri)
        {
            if (!uri.IsAbsoluteUri || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new FormatException("HTTP(S)のURLを指定してください。");
            var builder = new UriBuilder(uri) { Fragment = "" };
            if (Is5chHost(uri.Host))
            {
                builder.Host = Regex.Replace(uri.Host, @"(?:5ch|2ch)\.(?:net|io)$", "5ch.io", RegexOptions.IgnoreCase);
                builder.Scheme = "https";
                builder.Port = -1;
            }
            return builder.Uri;
        }
        public static Uri NormalizeBoard(Uri uri)
        {
            uri = Normalize(uri);
            var board = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrEmpty(board) || !Regex.IsMatch(board, "^[a-zA-Z0-9_]+$"))
                throw new FormatException("板URLが不正です。");
            return new UriBuilder(uri) { Path = "/" + board + "/", Query = "" }.Uri;
        }
        public static ThreadAddress Parse(string url)
        {
            var uri = Normalize(new Uri(url, UriKind.Absolute));
            var match = Regex.Match(uri.AbsolutePath, @"^/(?:test/)?read\.cgi/(?:c/)?(?<board>[a-zA-Z0-9_]+)/(?<id>[0-9]{9,12})(?:/|$)");
            if (!match.Success)
                throw new FormatException("read.cgi形式のスレッドURLを指定してください。");
            return new ThreadAddress(new Uri(uri, "/" + match.Groups["board"].Value + "/"), match.Groups["id"].Value);
        }
        public static bool TryParse(string url, out ThreadAddress address)
        {
            try
            {
                address = Parse(url);
                return true;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException)
            {
                address = null;
                return false;
            }
        }
    }
}
