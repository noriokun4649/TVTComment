using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TVTComment.Model
{
    sealed class SettingsAutosave : IDisposable
    {
        private readonly TVTComment model;
        private readonly SettingFileReaderWriter<TVTCommentSettings> file;
        private readonly INotifyPropertyChanged[] values;
        private readonly INotifyCollectionChanged[] lists;
        private Task writing = Task.CompletedTask;
        private Task nextSave = Task.CompletedTask;
        private bool pending, disposed;

        public SettingsAutosave(TVTComment model, SettingFileReaderWriter<TVTCommentSettings> file)
        {
            this.model = model;
            this.file = file;
            var chat = model.ChatModule;
            var defaults = model.DefaultChatCollectServiceModule;
            values = [chat.ChatPreserveCount, chat.ClearChatsOnChannelChange, chat.NgCommentNotShow,
                chat.AddChannelLogOnChannelChange, chat.CommentLogSameUserHighlight, chat.NXJikkyoImportDisable,
                chat.UiFlashingDeterrence, defaults.IsEnabled, model.ChatOpacity, model.ChatPostSplitterLeftSize,
                model.ChatPostSplitterRightSize, model.Settings.Niconico,
                .. model.ChatServices.OfType<ChatService.NichanChatService>().Single().SettingValues];
            lists = [chat.ChatModRules, defaults.LiveChatCollectService, defaults.RecordChatCollectService,
                model.ChatPostMailTextExamples];
            foreach (var value in values) value.PropertyChanged += Changed;
            foreach (var list in lists) list.CollectionChanged += Changed;
        }

        private void Changed(object sender, EventArgs e) => RequestSave();

        internal async void RequestSave()
        {
            if (pending || model.State != TVTCommentState.Working) return;
            pending = true;
            // 操作単位での変更の集約と、連続操作時の書き込み頻度の抑制
            await Task.Yield();
            await nextSave;
            if (disposed || model.State != TVTCommentState.Working) return;
            pending = false;
            model.ChatModule.SaveSettings();
            model.DefaultChatCollectServiceModule.SaveSettings();
            writing = WriteAsync();
            nextSave = Task.WhenAll(writing, Task.Delay(100));
        }

        private async Task WriteAsync()
        {
            var settings = model.Settings;
            settings.ChatPostMailTextExamples = model.ChatPostMailTextExamples.ToArray();
            settings.ChatPostSplitterRightSize = model.ChatPostSplitterRightSize.Value;
            settings.ChatPostSplitterLeftSize = model.ChatPostSplitterLeftSize.Value;
            try { await file.Write(settings).ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // 既存ファイルの保持と、次回の設定変更時または終了時の再試行
                Trace.TraceWarning($"設定を保存できませんでした: {e.Message}");
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var value in values) value.PropertyChanged -= Changed;
            foreach (var list in lists) list.CollectionChanged -= Changed;
            // UIスレッドに依存しない書き込みの完了待ちと、終了時に確定した設定の最終保存
            writing.GetAwaiter().GetResult();
            WriteAsync().GetAwaiter().GetResult();
        }
    }
}
