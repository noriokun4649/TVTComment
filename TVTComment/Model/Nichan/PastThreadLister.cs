using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nichan
{
    sealed class PastThreadLister
    {
        private readonly Uri board;
        private readonly ArchivedThreadListRetriever retriever;
        private readonly PastThreadRepository repository;
        private readonly TimeSpan backTime;
        private List<Thread> activeThreads = new List<Thread>();
        private DateTime subjectExpires;
        public PastThreadLister(Uri board, TimeSpan backTime, PastThreadRepository repository)
        {
            this.board = ThreadAddress.NormalizeBoard(board);
            this.backTime = backTime > TimeSpan.Zero ? backTime : TimeSpan.FromHours(3);
            this.repository = repository;
            retriever = new ArchivedThreadListRetriever(this.board);
        }
        public async IAsyncEnumerable<Thread> GetBetween(
            DateTimeOffset startTime, DateTimeOffset endTime, string[] keywords,
            [EnumeratorCancellation] CancellationToken token
        )
        {
            var start = startTime - backTime;
            var candidates = new List<Thread>();
            try
            {
                candidates.AddRange(await retriever.GetThreadsCreatedAt(start, endTime, token).ConfigureAwait(false));
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
            { }
            if (DateTime.UtcNow >= subjectExpires)
            {
                try
                {
                    activeThreads = await BoardClient.Shared.GetThreadsAsync(board, token).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                { }
                subjectExpires = DateTime.UtcNow.AddMinutes(1);
            }
            candidates.AddRange(activeThreads.Where(t => long.TryParse(t.Name, out var id) && start.ToUnixTimeSeconds() <= id && id <= endTime.ToUnixTimeSeconds()));
            var normalizedKeywords = keywords.Select(x => x.ToLowerInvariant().Normalize(NormalizationForm.FormKD)).ToArray();
            foreach (var candidate in candidates.DistinctBy(t => ThreadAddress.Parse(t.Uri.AbsoluteUri).Identity))
            {
                var title = (candidate.Title ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormKD);
                if (normalizedKeywords.Length > 0 && !normalizedKeywords.Any(title.Contains))
                    continue;
                var thread = await repository.GetAsync(candidate.Uri.AbsoluteUri, token).ConfigureAwait(false);
                var range = PastThreadRepository.GetTimeRange(thread);
                if (range.HasValue && startTime <= range.Value.Last && range.Value.First <= endTime)
                    yield return thread;
            }
        }
    }
}
