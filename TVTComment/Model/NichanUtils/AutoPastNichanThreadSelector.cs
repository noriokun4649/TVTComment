using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TVTComment.Model.NichanUtils
{
    class AutoPastNichanThreadSelector : INichanThreadSelector
    {
        public AutoPastNichanThreadSelector(ThreadResolver threadResolver, TimeSpan getTimeSpan, TimeSpan backTime, Nichan.PastThreadRepository repository)
        {
            this.threadResolver = threadResolver;
            this.getTimeSpan = getTimeSpan;
            this.backTime = backTime;
            this.repository = repository;
        }

        public async IAsyncEnumerable<string> Get(ChannelInfo channel, DateTimeOffset time, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            IEnumerable<MatchingThread> matchingThreads = threadResolver.Resolve(channel, false) ?? Enumerable.Empty<MatchingThread>();

            foreach (var thread in matchingThreads)
            {
                string[] keywords = thread.ThreadTitleKeywords?.ToArray() ?? Array.Empty<string>();
                var board = Nichan.ThreadAddress.NormalizeBoard(thread.BoardUri);
                var key = board.AbsoluteUri;

                if (!pastThreadListerCache.TryGetValue(key, out var threadLister))
                {
                    if (pastThreadListerCache.Count >= 32)
                        pastThreadListerCache.Remove(pastThreadListerCache.Keys.First());
                    threadLister = new Nichan.PastThreadLister(board, backTime, repository);
                    pastThreadListerCache.Add(key, threadLister);
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
        private readonly Nichan.PastThreadRepository repository;
        private readonly Dictionary<string, Nichan.PastThreadLister> pastThreadListerCache = new Dictionary<string, Nichan.PastThreadLister>();
    }
}
