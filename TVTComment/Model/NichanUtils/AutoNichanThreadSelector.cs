using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model.NichanUtils
{
    class AutoNichanThreadSelector : INichanThreadSelector
    {
        private static readonly HttpClient httpClient = new HttpClient();

        private readonly ThreadResolver threadResolver;

        public AutoNichanThreadSelector(ThreadResolver threadResolver)
        {
            this.threadResolver = threadResolver;
        }

        public async IAsyncEnumerable<string> Get(
            ChannelInfo channel, DateTimeOffset time, [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            IEnumerable<MatchingThread> matchingThreads = threadResolver.Resolve(channel, false);
            if (matchingThreads == null) yield break;

            foreach (var entry in matchingThreads)
            {
                var keywords = entry.ThreadTitleKeywords.Select(
                    x => x.ToLower().Normalize(NormalizationForm.FormKD)
                ).ToList();

                string boardUri = entry.BoardUri.ToString();
                var uri = new Uri(boardUri);
                string boardHost = $"{uri.Scheme}://{uri.Host}";
                string boardName = uri.Segments[1];
                if (boardName.EndsWith('/'))
                    boardName = boardName[..^1];

                byte[] subjectBytes = await httpClient.GetByteArrayAsync(
                    $"{boardHost}/{boardName}/subject.txt", cancellationToken
                );
                string subject = Encoding.GetEncoding(932).GetString(subjectBytes);

                using var textReader = new StringReader(subject);
                var threadsInBoard = Nichan.SubjecttxtParser.ParseFromStream(textReader);
                var urls = threadsInBoard.Where(
                    x => x.ResCount <= 1000
                ).Select(
                    x => { x.Title = x.Title.ToLower().Normalize(NormalizationForm.FormKD); return x; }
                ).Where(
                    x => keywords.Count == 0 || keywords.Any(keyword => x.Title.Contains(keyword))
                ).OrderByDescending(x => x.ResCount).Take(5).Select(
                    x => $"{boardHost}/test/read.cgi/{boardName}/{x.Name}/l50"
                );

                await foreach (var url in urls)
                {
                    yield return url;
                }
            }
        }
    }
}
