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
    class KeywordNichanThreadSelector : INichanThreadSelector
    {

        public string BoardUri { get; }
        public IEnumerable<string> Keywords { get; }

        public KeywordNichanThreadSelector(string boardUri, IEnumerable<string> keywords)
        {
            BoardUri = Nichan.ThreadAddress.NormalizeBoard(new Uri(boardUri)).AbsoluteUri;
            Keywords = keywords.Select(x => x.ToLower().Normalize(NormalizationForm.FormKD)).ToArray();
        }

        public async IAsyncEnumerable<string> Get(ChannelInfo channel, DateTimeOffset time, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var threads = await Nichan.BoardClient.Shared.GetThreadsAsync(new Uri(BoardUri), cancellationToken).ConfigureAwait(false);
            var threadsInBoard = threads.Where(x => x.ResCount <= 1000)
                .Select(
                    x => { x.Title = x.Title.ToLower().Normalize(NormalizationForm.FormKD); return x; }
                ).Where(
                    x => Keywords.All(keyword => x.Title.Contains(keyword))
                ).Select(
                    x => x.Uri.AbsoluteUri
                );

            foreach (var thread in threadsInBoard)
            {
                yield return thread;
            }
        }
    }
}
