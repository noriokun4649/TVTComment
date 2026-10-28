using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TVTComment.Model.ChatCollectService
{
    sealed class PastNichanChatCollectService : IChatCollectService
    {
        public string Name => "2ch過去ログ";
        public ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public bool CanPost => false;
        private readonly NichanUtils.INichanThreadSelector selector;
        private readonly Nichan.PastThreadRepository repository;
        private readonly TimeSpan interval;
        private readonly object sync = new object();
        private readonly SemaphoreSlim queryLock = new SemaphoreSlim(1);
        private readonly ReplayCursor cursor = new ReplayCursor();
        private CancellationTokenSource cancellation;
        private Task worker = Task.CompletedTask;
        private Task cleanupTask = Task.CompletedTask;
        private List<Nichan.Thread> threads = new List<Nichan.Thread>();
        private ChannelInfo channel;
        private DateTime loadedUntil, retryAt;
        private int generation;
        private bool loaded, disposed;
        private string status = "再生時刻待ち";

        public PastNichanChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry entry,
            NichanUtils.INichanThreadSelector selector,
            TimeSpan interval, Nichan.PastThreadRepository repository
        )
        {
            ServiceEntry = entry;
            this.selector = selector;
            this.interval = interval;
            this.repository = repository;
        }

        public string GetInformationText()
        {
            lock (sync)
                return status + "\n" + string.Join("\n", threads.Select(x => $"{x.Title} ({x.ResCount}) {x.Uri}"));
        }
        public IEnumerable<Chat> GetChats(ChannelInfo channel, EventInfo _, DateTime time) => GetChats(channel, time);
        public IEnumerable<Chat> GetChats(ChannelInfo channel, DateTime time)
        {
            lock (sync)
            {
                if (disposed)
                    return Array.Empty<Chat>();
                time = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
                bool changed = this.channel == null || this.channel.NetworkId != channel.NetworkId || this.channel.ServiceId != channel.ServiceId;
                if (cursor.Observe(time, changed))
                {
                    generation++;
                    threads = new List<Nichan.Thread>();
                    loaded = false;
                    loadedUntil = retryAt = DateTime.MinValue;
                    StartQuery(channel, time);
                }
                else if (worker.IsCompleted && (loadedUntil == DateTime.MinValue || time >= loadedUntil - TimeSpan.FromSeconds(3)) && DateTime.UtcNow >= retryAt)
                {
                    // 取得が遅れた場合も、未検索の時間帯を飛ばさず続きから検索
                    var nextTime = loaded ? (time < loadedUntil ? time : loadedUntil) : cursor.DisplayedUntil.AddSeconds(1);
                    StartQuery(channel, nextTime);
                }
                this.channel = channel;
                if (!loaded)
                    return Array.Empty<Chat>();
                // 次の一覧の取得中に表示済み位置だけを先へ進めると、遅れて判明した次スレのレスを失う
                var displayUntil = time < loadedUntil ? time : loadedUntil;
                if (displayUntil <= cursor.DisplayedUntil)
                    return Array.Empty<Chat>();
                var result = new List<Chat>();
                foreach (var res in threads.SelectMany(t => t.Res).Where(r => r.Date.HasValue && r.Number <= 1000 && r.Text != null
                    && cursor.DisplayedUntil < r.Date.Value && r.Date.Value <= displayUntil).OrderBy(r => r.Date))
                {
                    var body = new XElement(res.Text);
                    foreach (var br in body.Descendants("br").ToArray())
                        br.ReplaceWith("\n");
                    result.Add(new Chat(
                        res.Date.Value, body.Value, Chat.PositionType.Normal, Chat.SizeType.Normal,
                        Color.White, res.UserId, res.Number
                    ));
                }
                cursor.Advance(displayUntil);
                return result;
            }
        }
        private void StartQuery(ChannelInfo channel, DateTime time)
        {
            var previous = cancellation;
            cleanupTask = Task.WhenAll(cleanupTask, worker, previous?.CancelAsync() ?? Task.CompletedTask)
                .ContinueWith(t =>
                {
                    _ = t.Exception;
                    previous?.Dispose();
                }, TaskScheduler.Default);
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            int version = generation;
            status = "過去ログを取得中…";
            worker = Task.Run(async () =>
            {
                bool entered = false;
                try
                {
                    await queryLock.WaitAsync(token).ConfigureAwait(false);
                    entered = true;
                    var urls = await selector.Get(channel, new DateTimeOffset(time, TimeSpan.FromHours(9)), token).ToListAsync(token).ConfigureAwait(false);
                    var result = new List<Nichan.Thread>();
                    foreach (var url in urls.DistinctBy(x => Nichan.ThreadAddress.Parse(x).Identity))
                    {
                        var thread = await repository.GetAsync(url, token).ConfigureAwait(false);
                        if (thread != null)
                            result.Add(thread);
                    }
                    token.ThrowIfCancellationRequested();
                    lock (sync)
                    {
                        if (version != generation || disposed)
                            return;
                        threads = result;
                        loaded = true;
                        loadedUntil = time + interval;
                        status = repository.LastError ?? (result.Count == 0 ? "対象の過去ログがありません" : "取得済");
                        retryAt = DateTime.UtcNow.AddSeconds(1);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                { }
                catch (Exception)
                {
                    lock (sync)
                        if (version == generation && !disposed)
                        {
                            status = "過去ログを取得できませんでした。30秒後に再試行します。";
                            retryAt = DateTime.UtcNow.AddSeconds(30);
                        }
                }
                finally
                {
                    if (entered)
                        queryLock.Release();
                }
            });
        }
        public Task PostChat(BasicChatPostObject post) => throw new NotSupportedException();
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                    return;
                disposed = true;
                generation++;
                var previous = cancellation;
                _ = Task.WhenAll(cleanupTask, worker, previous?.CancelAsync() ?? Task.CompletedTask)
                    .ContinueWith(t =>
                    {
                        _ = t.Exception;
                        previous?.Dispose();
                        repository.Dispose();
                    }, TaskScheduler.Default);
            }
        }
    }

    // TVTestの停止・再開時に生じる小さな逆行は表示済み位置を戻さない
    internal sealed class ReplayCursor
    {
        private DateTime? observed;
        public DateTime DisplayedUntil { get; private set; }
        public bool Observe(DateTime time, bool channelChanged)
        {
            bool seek = channelChanged || !observed.HasValue || time < observed.Value.AddSeconds(-2) || time > observed.Value.AddSeconds(10);
            if (seek)
                DisplayedUntil = time.AddSeconds(-1);
            observed = time;
            return seek;
        }
        public void Advance(DateTime time)
        {
            if (time > DisplayedUntil)
                DisplayedUntil = time;
        }
    }
}
