using ObservableUtils;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.IPC.IPCMessage;

namespace TVTComment.Model
{
    // ニコニコ実況過去ログの一括取得と録画先頭基準の10秒集計
    sealed class CommentActivityModule : IDisposable
    {
        private const int RequestMinutes = 3 * 24 * 60;
        private const int IntervalSeconds = 10;
        private readonly IPCModule ipc;
        private readonly ChatCollectServiceModule services;
        private readonly NiconicoUtils.JkIdResolver resolver;
        private readonly HttpClient client;
        private readonly IDisposable adjustmentSubscription;
        private readonly IDisposable enabledSubscription;
        private CancellationTokenSource cancellation;
        private Task worker = Task.CompletedTask;
        private Request request;
        private (int JkId, DateTime Start) cacheIdentity;
        private readonly Dictionary<DateTime, int> cache = new();
        private bool selected;
        private bool applyingSettings;
        private volatile bool disposed;
        public ObservableValue<bool> GraphEnabled { get; } = new ObservableValue<bool>(true);

        internal sealed class Request
        {
            public string Id;
            public DateTime Start;
            public int Duration;
            public ushort NetworkId, ServiceId;

            public static Request Parse(string[] fields)
            {
                if (fields.Length != 7 || fields[0].Length == 0 || fields[0].Length > 64 || fields[1].Length > 64
                    || !long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long start)
                    || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int duration) || duration <= 0
                    || !ushort.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out ushort nid)
                    || !ushort.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out _)
                    || !ushort.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out ushort sid)) return null;
                try
                {
                    var date = DateTimeOffset.FromUnixTimeMilliseconds(start).ToOffset(TimeSpan.FromHours(9)).DateTime;
                    _ = date.AddMilliseconds(duration);
                    return new Request { Id = fields[0], Start = date, Duration = duration, NetworkId = nid, ServiceId = sid };
                }
                catch (ArgumentOutOfRangeException) { return null; }
            }
        }

        public CommentActivityModule(IPCModule ipc, ChatCollectServiceModule services, NiconicoUtils.JkIdResolver resolver,
            HttpMessageHandler handler = null)
        {
            this.ipc = ipc;
            this.services = services;
            this.resolver = resolver;
            client = new HttpClient(handler ?? new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"TvtComment/{Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}");
            selected = IsSelected();
            ipc.MessageReceived += MessageReceived;
            ((INotifyCollectionChanged)services.RegisteredServices).CollectionChanged += SourcesChanged;
            adjustmentSubscription = services.TimeAdjustment.Subscribe(_ => Restart());
            enabledSubscription = GraphEnabled.Skip(1).Subscribe(async enabled =>
            {
                if (!applyingSettings)
                    await Send(default, "ActivitySetEnabled", enabled ? "1" : "0");
            });
        }

        public async void Start() => await Send(default, "ActivityReady");

        private bool IsSelected() => services.RegisteredServices.Any(x => x.ServiceEntry?.Id == "TsukumijimaJikkyoApi");

        private void SourcesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            bool value = IsSelected();
            if (selected == value) return;
            selected = value;
            Restart();
        }

        private void MessageReceived(IIPCMessage message)
        {
            if (disposed || message is not PanelIPCMessage panel) return;
            if (panel.Operation == "ActivitySettings" && panel.Arguments.Length == 1
                && (panel.Arguments[0] == "0" || panel.Arguments[0] == "1"))
            {
                // ネイティブ側の保存設定・コマンド操作との同期、返信ループの抑止
                applyingSettings = true;
                GraphEnabled.Value = panel.Arguments[0] == "1";
                applyingSettings = false;
            }
            else if (panel.Operation == "ActivityStop") { request = null; cancellation?.Cancel(); }
            else if (panel.Operation == "ActivityRequest")
            {
                var next = Request.Parse(panel.Arguments);
                if (next == null || next.Id == request?.Id) return;
                request = next;
                Restart();
            }
        }

        private void Restart()
        {
            if (disposed || request == null) return;
            var current = request;
            var start = current.Start + services.TimeAdjustment.Value;
            // UIスレッド上での専用対応表の参照と、取得処理への実況IDの受け渡し
            int jkId = selected ? resolver.Resolve(current.NetworkId, current.ServiceId) : -1;
            var previous = cancellation;
            previous?.Cancel();
            var previousWorker = worker;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            worker = Task.Run(async () =>
            {
                try
                {
                    await previousWorker.ConfigureAwait(false);
                    previous?.Dispose();
                    token.ThrowIfCancellationRequested();
                    if (jkId > 0 && cacheIdentity != (jkId, start))
                    {
                        cacheIdentity = (jkId, start);
                        cache.Clear();
                    }
                    await Collect(current, start, jkId, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception)
                {
                    await Send(token, "ActivityStatus", current.Id, "error", "グラフを集計できませんでした。表示を切り替えて再取得してください。").ConfigureAwait(false);
                }
            });
        }

        private async Task Collect(Request current, DateTime start, int jkId, CancellationToken token)
        {
            int minutes = (int)((current.Duration + 59999L) / 60000);
            int intervals = (int)Math.Ceiling(current.Duration / (IntervalSeconds * 1000.0));
            await Send(token, "ActivityReset", current.Id, Number(intervals)).ConfigureAwait(false);
            if (jkId <= 0)
            {
                await Send(token, "ActivityStatus", current.Id, jkId < 0 ? "empty" : "error", jkId < 0
                    ? "非公式ニコニコ実況過去ログをコメント元へ追加してください。" : "このチャンネルに対応する実況IDがありません。").ConfigureAwait(false);
                return;
            }
            var complete = new HashSet<int>();
            while (true)
            {
                string error = null;
                for (int offset = 0; offset < minutes; offset += RequestMinutes)
                {
                    token.ThrowIfCancellationRequested();
                    if (complete.Contains(offset)) continue;
                    var first = start.AddMinutes(offset);
                    var last = start.AddMilliseconds(Math.Min((offset + RequestMinutes) * 60000L, current.Duration));
                    try
                    {
                        int[] values = await ReadCached(jkId, first, last, token).ConfigureAwait(false);
                        // IPCの引数上限に合わせた集計済み件数の分割通知
                        for (int sent = 0; sent < values.Length; sent += 60)
                            await Send(token, "ActivityChunk", new[] { current.Id, Number(offset * 60 / IntervalSeconds + sent) }
                                .Concat(values.Skip(sent).Take(60).Select(Number)).ToArray()).ConfigureAwait(false);
                        complete.Add(offset);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception e) { error = e is OperationCanceledException ? "通信がタイムアウトしました。" : e.Message; }
                    int loaded = complete.Sum(x => Math.Min(RequestMinutes, minutes - x));
                    await Send(token, "ActivityStatus", current.Id, error != null ? "error" : loaded == minutes ? "ready" : "loading", error == null
                        ? $"非公式ニコニコ実況過去ログ · {loaded}/{minutes}分"
                        : $"一部を取得できませんでした（{loaded}/{minutes}分）。30秒後に再試行します。{error}").ConfigureAwait(false);
                }
                if (error == null) return;
                await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            }
        }

        private async Task<int[]> ReadCached(int jkId, DateTime start, DateTime end, CancellationToken token)
        {
            int count = (int)Math.Ceiling((end - start).TotalSeconds / IntervalSeconds);
            var values = new int[count];
            DateTime Boundary(int i) => i == count ? end : start.AddSeconds(i * IntervalSeconds);
            // 同じ録画先頭基準の確定済み区間と、終了時刻が一致する端数区間の再利用
            int known = 0;
            while (known < count && cache.TryGetValue(Boundary(known + 1), out values[known])) known++;
            if (known < count)
            {
                var tail = await ReadAsync(client, jkId, Boundary(known), end, token).ConfigureAwait(false);
                for (int i = known; i < count; i++) cache[Boundary(i + 1)] = values[i] = tail[i - known];
            }
            return values;
        }

        private static async Task<int[]> ReadAsync(HttpClient client, int jkId, DateTime start, DateTime end, CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            token = timeout.Token;
            long first = new DateTimeOffset(start, TimeSpan.FromHours(9)).ToUnixTimeSeconds();
            // 終了秒も含むAPI範囲と、同一秒内の短い録画に必要な最小の時刻差
            long last = Math.Max(first + 1, new DateTimeOffset(end, TimeSpan.FromHours(9)).ToUnixTimeSeconds());
            using var stream = await client.GetStreamAsync($"https://jikkyo.tsukumijima.net/api/kakolog/jk{jkId}?starttime={first}&endtime={last}&format=json", token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
            return Count(document.RootElement, start, end, token);
        }

        internal static int[] Count(JsonElement root, DateTime start, DateTime end, CancellationToken token)
        {
            if (end <= start || end - start > TimeSpan.FromMinutes(RequestMinutes)) throw new ArgumentOutOfRangeException(nameof(end));
            if (root.TryGetProperty("error", out var error)) throw new InvalidDataException(error.GetString());
            var values = new int[(int)Math.Ceiling((end - start).TotalSeconds / IntervalSeconds)];
            var seen = new HashSet<(string Thread, long Number)>();
            long Parse(JsonElement value) => long.Parse(value.ToString(), CultureInfo.InvariantCulture);
            foreach (var packet in root.GetProperty("packet").EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                if (!packet.TryGetProperty("chat", out var chat) || chat.TryGetProperty("deleted", out _) || !chat.TryGetProperty("vpos", out _)) continue;
                if (!seen.Add((chat.GetProperty("thread").ToString(), Parse(chat.GetProperty("no"))))) continue;
                long usec = chat.TryGetProperty("date_usec", out var value) ? Parse(value) : 0;
                if (usec < 0 || usec >= 1000000) throw new InvalidDataException("コメントの時刻が不正です。");
                var time = DateTimeOffset.FromUnixTimeSeconds(Parse(chat.GetProperty("date"))).ToOffset(TimeSpan.FromHours(9)).DateTime.AddTicks(usec * 10);
                if (start <= time && time < end)
                {
                    int index = (int)((time - start).Ticks / (TimeSpan.TicksPerSecond * IntervalSeconds));
                    if (values[index] < int.MaxValue) values[index]++;
                }
            }
            return values;
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private async Task Send(CancellationToken token, string operation, params string[] arguments)
        {
            if (disposed || token.IsCancellationRequested || (ipc.State != IPCModuleState.Connected && ipc.State != IPCModuleState.Receiving)) return;
            try { await ipc.Send(new PanelIPCMessage { Operation = operation, Arguments = arguments }).ConfigureAwait(false); }
            catch (InvalidOperationException) when (disposed || ipc.State == IPCModuleState.Disposing || ipc.State == IPCModuleState.Disposed) { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ipc.MessageReceived -= MessageReceived;
            ((INotifyCollectionChanged)services.RegisteredServices).CollectionChanged -= SourcesChanged;
            adjustmentSubscription.Dispose();
            enabledSubscription.Dispose();
            var previous = cancellation;
            previous?.Cancel();
            _ = worker.ContinueWith(t => { _ = t.Exception; previous?.Dispose(); client.Dispose(); }, TaskScheduler.Default);
        }
    }
}
