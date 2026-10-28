using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TVTComment.Model.ChatCollectService
{
    abstract class NichanChatCollectService : IChatCollectService
    {
        public class ChatPostObject : BasicChatPostObject
        {
            public string ThreadUri { get; }

            public ChatPostObject(string threadUri) : base("")
            {
                ThreadUri = threadUri;
            }
        }

        protected abstract string TypeName { get; }
        protected abstract Task<Nichan.Thread> GetThread(string url, CancellationToken token);
        protected virtual void ResetThreads() { }
        public string Name => TypeName + (threadSelector switch
        {
            NichanUtils.AutoNichanThreadSelector _ => " ([自動])",
            NichanUtils.FuzzyNichanThreadSelector fuzzy => $" ([類似] {fuzzy.ThreadTitleExample})",
            NichanUtils.KeywordNichanThreadSelector keyword => $" ([キーワード] {string.Join(", ", keyword.Keywords)})",
            NichanUtils.FixedNichanThreadSelector fix => $" ([固定] {string.Join(", ", fix.Uris)})",
            _ => ""
        });
        public ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }
        public bool CanPost => true;
        protected readonly NichanUtils.INichanThreadSelector threadSelector;
        private readonly TimeSpan initialResInterval, initialSearchInterval;
        private TimeSpan ResInterval => ClampInterval((ServiceEntry.Owner as ChatService.NichanChatService)?.ResCollectInterval ?? initialResInterval);
        private TimeSpan SearchInterval => ClampInterval((ServiceEntry.Owner as ChatService.NichanChatService)?.ThreadSearchInterval ?? initialSearchInterval);
        private static TimeSpan ClampInterval(TimeSpan value) =>
            value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) :
            value > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : value;
        private readonly object sync = new object();
        private readonly SemaphoreSlim loopLock = new SemaphoreSlim(1);
        private CancellationTokenSource cancellation;
        private Task worker;
        private ChannelInfo channel;
        private DateTime currentTime;
        private int generation;
        private bool disposed;
        private readonly ConcurrentQueue<(int Generation, Chat Chat)> queue = new ConcurrentQueue<(int, Chat)>();
        private readonly List<Chat> displayBuffer = new List<Chat>();
        private readonly List<(DateTime Post, DateTime Received)> timing = new List<(DateTime, DateTime)>();
        private List<ThreadState> currentThreads = new List<ThreadState>();
        private string status = "チャンネル情報待ち";

        private sealed class ThreadState
        {
            public string Uri;
            public string Title;
            public int LastNumber;
            public DateTime RetryAt;
            public int Failures;
            public string Status = "";
        }

        protected NichanChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry entry,
            TimeSpan resInterval, TimeSpan searchInterval,
            NichanUtils.INichanThreadSelector selector
        )
        {
            ServiceEntry = entry;
            initialResInterval = resInterval;
            initialSearchInterval = searchInterval;
            threadSelector = selector;
        }

        public string GetInformationText()
        {
            lock (sync)
                return status + "\n" + string.Join("\n", currentThreads.Select(x => $"{x.Title ?? "[取得待ち]"} ({x.LastNumber}) {x.Status}\n{x.Uri}"));
        }
        public IEnumerable<Nichan.Thread> CurrentThreads
        {
            get
            {
                lock (sync)
                    return currentThreads.Select(x => new Nichan.Thread
                    {
                        Uri = new Uri(x.Uri),
                        Title = x.Title,
                        ResCount = x.LastNumber
                    }).ToArray();
            }
        }
        public IEnumerable<Chat> GetChats(ChannelInfo channel, EventInfo _, DateTime time) => GetChats(channel, time);
        public IEnumerable<Chat> GetChats(ChannelInfo channel, DateTime time)
        {
            if (disposed)
                return Array.Empty<Chat>();
            lock (sync)
            {
                currentTime = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
                if (this.channel == null || this.channel.NetworkId != channel.NetworkId || this.channel.ServiceId != channel.ServiceId)
                {
                    var previous = cancellation;
                    var oldTask = Task.WhenAll(worker ?? Task.CompletedTask, previous?.CancelAsync() ?? Task.CompletedTask);
                    _ = oldTask.ContinueWith(t =>
                    {
                        _ = t.Exception;
                        previous?.Dispose();
                    }, TaskScheduler.Default);
                    this.channel = channel;
                    var version = ++generation;
                    cancellation = new CancellationTokenSource();
                    var token = cancellation.Token;
                    currentThreads = new List<ThreadState>();
                    displayBuffer.Clear();
                    timing.Clear();
                    worker = Task.Run(() => CollectAsync(channel, version, token));
                }
            }
            var maxAge = TimeSpan.FromSeconds(Math.Max(30, ResInterval.TotalSeconds * 2));
            while (queue.TryDequeue(out var item))
            {
                if (item.Generation != generation || (time - item.Chat.Time).Duration() > maxAge)
                    continue;
                displayBuffer.Add(item.Chat);
                timing.Add((item.Chat.Time, time));
            }
            timing.RemoveAll(x => time - x.Received > maxAge);
            var delay = timing.Select(x => x.Received - x.Post).DefaultIfEmpty(TimeSpan.Zero).Max();
            var result = displayBuffer.Where(x => x.Time + delay <= time).ToArray();
            displayBuffer.RemoveAll(x => x.Time + delay <= time || time - x.Time > maxAge);
            return result;
        }

        private async Task CollectAsync(ChannelInfo selectedChannel, int version, CancellationToken token)
        {
            bool entered = false;
            try
            {
                await loopLock.WaitAsync(token).ConfigureAwait(false);
                entered = true;
                ResetThreads();
                var known = new Dictionary<string, ThreadState>();
                var selected = new List<ThreadState>();
                DateTime nextSearch = DateTime.MinValue, nextRes = DateTime.MinValue;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow >= nextSearch)
                    {
                        try
                        {
                            DateTime time;
                            lock (sync)
                                time = currentTime;
                            // 列挙・通信・解析まで一度だけ完了させてから共有状態へ反映
                            var urls = await threadSelector.Get(selectedChannel, new DateTimeOffset(time, TimeSpan.FromHours(9)), token).ToListAsync(token).ConfigureAwait(false);
                            selected = new List<ThreadState>();
                            foreach (var address in urls.Select(Nichan.ThreadAddress.Parse).Where(x => !x.IsAnnouncement).DistinctBy(x => x.Identity))
                            {
                                if (!known.TryGetValue(address.Identity, out var state))
                                    known[address.Identity] = state = new ThreadState { Uri = address.ThreadUri.AbsoluteUri };
                                selected.Add(state);
                            }
                            if (known.Count > 256)
                                foreach (var key in known.Where(x => !selected.Contains(x.Value)).Select(x => x.Key).Take(known.Count - 256).ToArray())
                                    known.Remove(key);
                            lock (sync)
                                if (version == generation)
                                {
                                    currentThreads = selected;
                                    status = selected.Count == 0 ? "対応スレがありません" : "取得中";
                                }
                        }
                        catch (Exception e) when (IsFetchError(e))
                        {
                            lock (sync)
                                if (version == generation)
                                    status = "スレ一覧を更新できませんでした: " + e.Message;
                        }
                        nextSearch = DateTime.UtcNow + SearchInterval;
                    }
                    if (DateTime.UtcNow >= nextRes)
                    {
                        foreach (var state in selected)
                        {
                            if (DateTime.UtcNow < state.RetryAt)
                                continue;
                            try
                            {
                                var thread = await GetThread(state.Uri, token).ConfigureAwait(false);
                                token.ThrowIfCancellationRequested();
                                foreach (var res in thread.Res.Where(x => x.Number > state.LastNumber && x.Number <= 1000 && x.Date.HasValue && x.Text != null))
                                {
                                    var body = new XElement(res.Text);
                                    foreach (var br in body.Descendants("br").ToArray())
                                        br.ReplaceWith("\n");
                                    queue.Enqueue((version, new Chat(
                                        res.Date.Value, body.Value, Chat.PositionType.Normal, Chat.SizeType.Normal,
                                        Color.White, res.UserId, res.Number
                                    )));
                                }
                                lock (sync)
                                {
                                    state.Title = thread.Title;
                                    state.LastNumber = Math.Max(state.LastNumber, thread.Res.Select(x => x.Number).DefaultIfEmpty(0).Max());
                                    state.Status = "";
                                    state.Failures = 0;
                                    if (thread.ResCount > 1000)
                                    {
                                        state.RetryAt = DateTime.MaxValue;
                                        state.Status = "終了";
                                    }
                                }
                            }
                            catch (Nichan.ThreadUnavailableException)
                            {
                                lock (sync)
                                {
                                    state.Status = "終了または取得不能";
                                    state.RetryAt = DateTime.MaxValue;
                                }
                                // 自動選択等は次の一覧更新で次スレへ進む
                            }
                            catch (Exception e) when (IsFetchError(e))
                            {
                                var wait = (e as Nichan.ResponseApiClientException)?.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(++state.Failures, 6))));
                                lock (sync)
                                {
                                    state.Status = e.Message;
                                    state.RetryAt = DateTime.UtcNow + wait;
                                }
                            }
                        }
                        nextRes = DateTime.UtcNow + ResInterval;
                    }
                    var until = (nextSearch < nextRes ? nextSearch : nextRes) - DateTime.UtcNow;
                    await Task.Delay(until > TimeSpan.Zero ? until : TimeSpan.FromMilliseconds(1), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { }
            catch (Exception e)
            {
                lock (sync)
                    if (version == generation)
                        status = "取得処理を停止しました: " + e.Message;
            }
            finally
            {
                if (entered)
                    loopLock.Release();
            }
        }

        private static bool IsFetchError(Exception e) =>
            e is HttpRequestException || e is Nichan.NichanException || e is FormatException ||
            e is IOException || e is TaskCanceledException || e is System.Xml.XmlException;

        public Task PostChat(BasicChatPostObject post)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Nichan.ThreadAddress.Parse(((ChatPostObject)post).ThreadUri).ThreadUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is FormatException)
            {
                throw new ChatPostException("スレッドをブラウザーで開けませんでした。", e);
            }
            return Task.CompletedTask;
        }
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            var previous = cancellation;
            _ = Task.WhenAll(worker ?? Task.CompletedTask, previous?.CancelAsync() ?? Task.CompletedTask)
                .ContinueWith(t =>
                {
                    _ = t.Exception;
                    previous?.Dispose();
                }, TaskScheduler.Default);
        }
    }

    class HTMLNichanChatCollectService : NichanChatCollectService
    {
        protected override string TypeName => "2chHTML";
        private static readonly HttpClient client = Nichan.BoardClient.CreateHttpClient();

        public HTMLNichanChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry entry,
            TimeSpan res, TimeSpan search,
            NichanUtils.INichanThreadSelector selector
        ) : base(entry, res, search, selector)
        {
        }

        protected override async Task<Nichan.Thread> GetThread(string url, CancellationToken token)
        {
            using var reader = new StringReader(await client.GetStringAsync(url, token).ConfigureAwait(false));
            var thread = Nichan.ThreadParser.ParseFromStream(reader);
            thread.Uri = new Uri(url);
            return thread;
        }
    }
    class DATNichanChatCollectService : NichanChatCollectService
    {
        protected override string TypeName => "2chDAT";
        private readonly Nichan.ApiClient client;
        private readonly Dictionary<string, Nichan.DatThreadLoader> loaders = new Dictionary<string, Nichan.DatThreadLoader>();

        public DATNichanChatCollectService(
            ChatCollectServiceEntry.IChatCollectServiceEntry entry,
            TimeSpan res, TimeSpan search,
            NichanUtils.INichanThreadSelector selector, Nichan.ApiClient client
        ) : base(entry, res, search, selector)
        {
            this.client = client;
        }

        protected override void ResetThreads() => loaders.Clear();
        protected override async Task<Nichan.Thread> GetThread(string url, CancellationToken token)
        {
            var address = Nichan.ThreadAddress.Parse(url);
            if (!loaders.TryGetValue(address.Identity, out var loader))
            {
                if (loaders.Count >= 128)
                    loaders.Remove(loaders.Keys.First());
                loaders[address.Identity] = loader = new Nichan.DatThreadLoader(address);
            }
            await loader.Update(client, token).ConfigureAwait(false);
            return loader.Thread;
        }
    }
}
