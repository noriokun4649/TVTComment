using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TVTComment.Model.NichanUtils
{
    class AutoPastNichanThreadSelector : INichanThreadSelector
    {
        public AutoPastNichanThreadSelector(ThreadResolver threadResolver, TimeSpan getTimeSpan, TimeSpan backTime, string pastUserAgent)
        {
            this.threadResolver = threadResolver;
            this.getTimeSpan = getTimeSpan;
            this.backTime = backTime;
            this.pastUserAgent = pastUserAgent;
        }

        public async IAsyncEnumerable<string> Get(ChannelInfo channel, DateTimeOffset time, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            IEnumerable<MatchingThread> matchingThreads = threadResolver.Resolve(channel, false);

            foreach (var thread in matchingThreads)
            {
                string[] keywords = thread.ThreadTitleKeywords?.Select(
                    x => x.ToLower().Normalize(NormalizationForm.FormKD)
                )?.ToArray() ?? Array.Empty<string>();
                (string server, string board) = GetServerAndBoardFromBoardUrl(thread.BoardUri.ToString());

                if (!pastThreadListerCache.TryGetValue(board, out var threadLister))
                {
                    threadLister = new Nichan.PastThreadLister(board, server, backTime, pastUserAgent);
                    await threadLister.Initialize(cancellationToken).ConfigureAwait(false);
                    pastThreadListerCache.Add(board, threadLister);
                }

                DateTimeOffset startTime = time;
                var threads = threadLister.GetBetween(startTime, startTime + getTimeSpan, keywords, cancellationToken);
                await foreach (var x in threads)
                {
                    yield return x.Uri.ToString();
                }
            }
        }

        private readonly ThreadResolver threadResolver;
        private readonly TimeSpan getTimeSpan;
        private readonly TimeSpan backTime;
        private readonly string pastUserAgent;
        private readonly Dictionary<string, Nichan.PastThreadLister> pastThreadListerCache = new Dictionary<string, Nichan.PastThreadLister>();

        private static readonly Regex reBoardUrl = new Regex(@"//(?<server>[^.]*)\.\dch\.(net|sc)/(?<board>[^/]*)(^|/.*)");
        private static (string server, string board) GetServerAndBoardFromBoardUrl(string boardUrl)
        {
            Match match = reBoardUrl.Match(boardUrl);
            return (match.Groups["server"].Value, match.Groups["board"].Value);
        }
    }
}
