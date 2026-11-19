using ObservableUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.NiconicoUtils;

namespace TVTComment.Model.ChatCollectService
{
    sealed class NicoJCatchChatCollectService(ChatCollectServiceEntry.IChatCollectServiceEntry entry, JkIdResolver resolver,
        ChannelDatabase channels, ChatService.NiconicoChatServiceSettings settings) : IChatCollectService
    {
        public string Name => ServiceEntry.Name;
        public ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; } = entry;
        public bool CanPost => false;
        private readonly ReplayCursor cursor = new();
        private readonly Dictionary<string, DateTime> displayed = new();
        private Queue<NicoJCatchLogReader.Comment> pending = new();
        private CancellationTokenSource playback = new();
        private Task worker = Task.CompletedTask;
        private (ushort Network, ushort Service, string Folder) source;
        private int jkId;
        private DateTime readFrom, loadedUntil, refreshAt;
        private const string Loading = "過去ログを読み込み中…";
        private readonly ObservableValue<string> status = new(Loading);
        private bool readFailed;

        public IObservable<string> StatusChanges => status;
        public string GetInformationText() => status.Value;
        public Task PostChat(BasicChatPostObject post) => throw new NotSupportedException();

        public IEnumerable<Chat> GetChats(ChannelInfo channel, EventInfo _, DateTime time)
        {
            if (playback == null) return Array.Empty<Chat>();
            time = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
            var current = (channel.NetworkId, channel.ServiceId, settings.NicoJCatchLogFolder);
            if (cursor.Observe(time, current != source))
            {
                StopReading();
                playback = new CancellationTokenSource();
                worker = Task.CompletedTask;
                var previousTarget = (source.Folder, jkId);
                source = current;
                // 録画でネットワークIDが不明な場合の、サービスIDが一致する局の照合
                jkId = source.Network != 0 ? resolver.Resolve(source.Network, source.Service)
                    : channels.ChannelList.Where(c => c.ServiceId == source.Service)
                        .Select(c => resolver.Resolve(c.NetworkId, c.ServiceId)).FirstOrDefault(id => id != 0);
                if (previousTarget != (source.Folder, jkId)) readFailed = false;
                pending.Clear();
                displayed.Clear();
                readFrom = cursor.DisplayedUntil;
                loadedUntil = refreshAt = DateTime.MinValue;
                if (!readFailed)
                    status.Value = string.IsNullOrWhiteSpace(source.Folder) ? "設定の「NicojCatch」で親フォルダを指定してください"
                        : jkId == 0 && source.Service != 0 ? "対応する実況IDがありません" : Loading;
            }
            if (jkId == 0 || string.IsNullOrWhiteSpace(source.Folder)) return Array.Empty<Chat>();
            if (!readFailed && worker.IsCompleted && DateTime.UtcNow >= refreshAt)
                worker = Refresh(source.Folder, jkId, time.AddMinutes(1), playback.Token);

            List<Chat> result = null;
            while (pending.TryPeek(out var comment) && comment.Time <= time)
            {
                pending.Dequeue();
                if (displayed.TryAdd(comment.Identity, comment.Time))
                    (result ??= new List<Chat>()).Add(comment.ToChat());
            }
            cursor.Advance(time < loadedUntil ? time : loadedUntil);
            return result is null ? Array.Empty<Chat>() : result;
        }

        private async Task Refresh(string folder, int id, DateTime until, CancellationToken token)
        {
            var from = readFrom;
            // 追記確認のための前回検索時点からの再読と、停止中の同一秒の保持
            var nextFrom = cursor.DisplayedUntil.AddSeconds(-1);
            try
            {
                // ファイルI/Oのみワーカー側、再生状態の反映は呼び出し元のUIスレッド
                var batch = await Task.Run(() => NicoJCatchLogReader.ReadAsync(folder, id, from, until, token), token);
                if (token.IsCancellationRequested) return;
                foreach (var key in displayed.Where(x => x.Value <= from).Select(x => x.Key).ToArray())
                    displayed.Remove(key);
                pending = batch.Comments;
                loadedUntil = until;
                if (nextFrom > readFrom) readFrom = nextFrom;
                status.Value = batch.Files.Length == 0 ? $"jk{id}: 対象の過去ログがありません"
                    : $"jk{id}: {pending.Count}コメント\n" + string.Join("\n", batch.Files);
                refreshAt = DateTime.UtcNow.AddSeconds(5);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (token.IsCancellationRequested) return;
                readFailed = true;
                status.Value = $"jk{id}: ログを読み込めません。\n{e.Message}";
            }
        }

        private void StopReading()
        {
            playback?.Cancel();
            playback?.Dispose();
            playback = null;
        }

        public void Dispose() => StopReading();
    }
}
