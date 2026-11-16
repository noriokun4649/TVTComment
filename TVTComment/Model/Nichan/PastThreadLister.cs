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
            var normalizedKeywords = keywords.Select(x => x.ToLowerInvariant().Normalize(NormalizationForm.FormKD)).ToArray();
            var loads = new Dictionary<string, Task<Thread>>();
            var seen = new HashSet<string>();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            async Task<Thread> LoadThreadAsync(Uri uri)
            {
                try
                {
                    return await repository.GetAsync(uri.AbsoluteUri, cancellation.Token).ConfigureAwait(false);
                }
                catch
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    throw;
                }
            }
            void LoadCandidate(Thread candidate)
            {
                var identity = ThreadAddress.Parse(candidate.Uri.AbsoluteUri).Identity;
                if (!seen.Add(identity))
                    return;
                var title = (candidate.Title ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormKD);
                if (normalizedKeywords.Length == 0 || normalizedKeywords.Any(title.Contains))
                    loads.Add(identity, LoadThreadAsync(candidate.Uri));
            }
            // 一覧検索中の本文取得と、現行スレッド一覧の並行取得
            var activeTask = UpdateActiveThreadsAsync(cancellation.Token);
            try
            {
                var candidates = new List<Thread>();
                try
                {
                    candidates.AddRange(await retriever.GetThreadsCreatedAt(start, endTime, cancellation.Token, LoadCandidate).ConfigureAwait(false));
                }
                catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                { }
                await activeTask.ConfigureAwait(false);
                candidates.AddRange(activeThreads.Where(t => long.TryParse(t.Name, out var id) && start.ToUnixTimeSeconds() <= id && id <= endTime.ToUnixTimeSeconds()));
                var matching = candidates.DistinctBy(t => ThreadAddress.Parse(t.Uri.AbsoluteUri).Identity).ToArray();
                foreach (var candidate in matching)
                    LoadCandidate(candidate);
                await Task.WhenAll(loads.Values).ConfigureAwait(false);
                // 本文の完了順によらない、一覧順での結果の確定
                foreach (var candidate in matching)
                {
                    token.ThrowIfCancellationRequested();
                    if (!loads.TryGetValue(ThreadAddress.Parse(candidate.Uri.AbsoluteUri).Identity, out var task))
                        continue;
                    var thread = await task.ConfigureAwait(false);
                    var range = PastThreadRepository.GetTimeRange(thread);
                    if (range.HasValue && startTime <= range.Value.Last && range.Value.First <= endTime)
                        yield return thread;
                }
            }
            finally
            {
                // 一覧取得の失敗や列挙中断時の、取得中タスクのキャンセルと完了待機
                await cancellation.CancelAsync().ConfigureAwait(false);
                try
                {
                    await Task.WhenAll(loads.Values.Cast<Task>().Append(activeTask)).ConfigureAwait(false);
                }
                catch (Exception)
                { }
            }
        }
        private async Task UpdateActiveThreadsAsync(CancellationToken token)
        {
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
        }
    }
}
