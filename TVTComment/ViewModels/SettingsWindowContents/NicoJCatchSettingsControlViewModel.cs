using CommunityToolkit.Mvvm.Input;
using ObservableUtils;
using System;
using System.IO;
using System.Windows.Input;
using TVTComment.ViewModels.Notifications;
using TVTComment.ViewModels.Services;

namespace TVTComment.ViewModels.SettingsWindowContents
{
    sealed class NicoJCatchSettingsControlViewModel
    {
        public ObservableValue<string> Folder { get; }
        public ICommand Apply { get; }

        public NicoJCatchSettingsControlViewModel(Model.TVTComment model, IDialogService dialogs)
        {
            var settings = model.Settings.Niconico;
            Folder = new ObservableValue<string>(settings.NicoJCatchLogFolder);
            Apply = new RelayCommand(() =>
            {
                var path = Environment.ExpandEnvironmentVariables((Folder.Value ?? "").Trim().Trim('"'));
                if (path.Length != 0 && !Path.IsPathFullyQualified(path))
                {
                    dialogs.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = "局別フォルダが入った親フォルダを絶対パスで指定してください。" });
                    return;
                }
                try { Folder.Value = settings.NicoJCatchLogFolder = path.Length == 0 ? "" : Path.GetFullPath(path); }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    dialogs.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = "フォルダのパスが不正です。\n" + e.Message });
                    return;
                }
                dialogs.ShowNotification(new Notification { Title = "TVTCommentメッセージ", Content = "適用しました" });
            });
        }
    }
}
