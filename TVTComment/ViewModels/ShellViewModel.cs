using ObservableUtils;
using CommunityToolkit.Mvvm.Input;
using TVTComment.ViewModels.Notifications;
using TVTComment.ViewModels.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TVTComment.ViewModels
{
    class ShellViewModel : ObservableObject, IDisposable
    {
        internal class Rect : ObservableObject
        {
            private double x = double.NaN, y = double.NaN;
            private double width = double.NaN, height = double.NaN;

            public double X
            {
                get { return x; }
                set { SetProperty(ref x, value); }
            }
            public double Y
            {
                get { return y; }
                set { SetProperty(ref y, value); }
            }
            public double Width
            {
                get { return width; }
                set { SetProperty(ref width, value); }
            }
            public double Height
            {
                get { return height; }
                set { SetProperty(ref height, value); }
            }
        }
        private readonly Rect windowPosition = new Rect();
        public Rect WindowPosition => windowPosition;

        private Model.ChannelInfo selectedChannel;
        public Model.ChannelInfo SelectedChannel
        {
            get { return selectedChannel; }
            set { SetProperty(ref selectedChannel, value); }
        }

        public DisposableReadOnlyObservableCollection<ShellContents.ChatCollectServiceAddListItemViewModel> ChatCollectServiceAddList { get; private set; }

        private readonly IObservable<long> updateTimer = Observable.Interval(new TimeSpan(0, 0, 5), new SynchronizationContextScheduler(SynchronizationContext.Current));
        public DisposableReadOnlyObservableCollection<ShellContents.ChatCollectServiceViewModel> ChatCollectServices { get; private set; }

        public ReadOnlyObservableCollection<Model.Chat> Chats => model.ChatModule?.Chats;

        private ReadOnlyObservableValue<Model.IForceValueData> forceValueData;
        public DisposableReadOnlyObservableCollection<ShellContents.ChannelListItemViewModel> Channels { get; private set; }

        public ReadOnlyObservableValue<DateTime?> CurrentPlayTime { get; private set; }
        public ReadOnlyObservableValue<Model.ChannelInfo> CurrentChannel { get; private set; }
        public ReadOnlyObservableValue<Model.EventInfo> CurrentEvent { get; private set; }
        public ObservableValue<bool> UseDefaultChatCollectService { get; private set; }

        public ObservableValue<string> WindowTitle { get; } = new ObservableValue<string>("TVTComment");
        public ObservableValue<double> WindowOpacity => BasicSettingControlViewModel?.WindowOpacity;
        public ObservableValue<bool> WindowTopmost => BasicSettingControlViewModel?.WindowTopmost;
        public ObservableValue<double> WindowFontSize => BasicSettingControlViewModel?.WindowFontSize;
        public ObservableValue<MainWindowTab> SelectedTab { get; } = new ObservableValue<MainWindowTab>();
        public WindowStateViewModel WindowPresentation { get; }
        public ShellContents.ChatPostControlViewModel ChatPostControlViewModel { get; set; }

        public Views.AttachedProperties.GridViewColumnSettingsBinder.ColumnInfo[] ChatListColumnInfos { set; get; } = Array.Empty<Views.AttachedProperties.GridViewColumnSettingsBinder.ColumnInfo>();

        public ICommand CloseApplicationCommand { get; private set; }
        public ICommand MinimizeWindowCommand => WindowPresentation.MinimizeCommand;
        public ICommand ChangeChannelCommand { get; private set; }
        public RelayCommand<ShellContents.ChatCollectServiceAddListItemViewModel> AddChatCollectServiceCommand { get; private set; }
        public RelayCommand<Model.ChatCollectService.IChatCollectService> RemoveChatCollectServiceCommand { get; private set; }
        public ICommand ClearChatsCommand { get; private set; }
        public ICommand AddWordNgCommand { get; private set; }
        public ICommand AddUserNgCommand { get; private set; }
        public ICommand CopyCommentCommand { get; private set; }
        public ICommand CopyUserCommand { get; private set; }
        public ICommand UpdateHoverdUserCommand { get; private set; }

        private readonly IDialogService dialogService;

        private ShellContents.BasicSettingControlViewModel basicSettingControlViewModel;
        public ShellContents.BasicSettingControlViewModel BasicSettingControlViewModel
        {
            get { return basicSettingControlViewModel; }
#pragma warning disable CS4014 // この呼び出しを待たないため、現在のメソッドの実行は、呼び出しが完了するまで続行します。呼び出しの結果に 'await' 演算子を適用することを検討してください。
            set { basicSettingControlViewModel = value; InitializeInternal(); }
#pragma warning restore CS4014 // この呼び出しを待たないため、現在のメソッドの実行は、呼び出しが完了するまで続行します。呼び出しの結果に 'await' 演算子を適用することを検討してください。
        }

        private bool initialized = false;
        private bool disposed = false;
        private bool addingChatCollectService;
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly Model.TVTComment model;
        private Model.ChatService.NiconicoChatService niconico;
        private TVTestPanelBridge panelBridge;

        public ShellViewModel(Model.TVTComment model, IDialogService dialogService)
        {
            this.model = model;
            this.dialogService = dialogService;
            WindowPresentation = new WindowStateViewModel(() => BasicSettingControlViewModel?.MinimizeOnTVTestPanelOpen.Value == true);

            Window mainWindow = Application.Current.MainWindow;
            mainWindow.MouseLeftButtonDown += (_, __) =>
            {
                // ボタン解放済みの押下イベントの除外
                if (Mouse.LeftButton != MouseButtonState.Pressed)
                    return;
                try
                {
                    mainWindow.DragMove();
                }
                catch (InvalidOperationException) when (Mouse.LeftButton != MouseButtonState.Pressed)
                {
                    // 押下確認後のボタン解放による例外の抑止
                }
            };

            model.ApplicationClose += CloseApplication;
        }

        private async Task InitializeInternal()
        {
            if (initialized) return;
            initialized = true;

            // モデルの初期化
            try
            {
                await model.Initialize();
                await BasicSettingControlViewModel.Initialize();
            }
            catch (Exception e)
            {
                dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = $"初期化で予期しないエラーが発生しました\n{e}" });
                CloseApplication();
                return;
            }
            if (model.State != Model.TVTCommentState.Working)
            {
                dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = "Viewer側（TVTestプラグイン側）と接続できませんでした" });
                CloseApplication();
                return;
            }

            // 表示関係の設定復元
            Model.Serialization.WindowPositionEntity rect = model.Settings.View.MainWindowPosition;
            WindowPosition.X = rect.X;
            WindowPosition.Y = rect.Y;
            WindowPosition.Width = rect.Width;
            WindowPosition.Height = rect.Height;

            ChatListColumnInfos = model.Settings.View.ChatListViewColumns?.Select(
                x => new Views.AttachedProperties.GridViewColumnSettingsBinder.ColumnInfo(x.Id, x.Width)
            ).ToArray();

            SelectedTab.Value = model.Settings.View.MainWindowTab;

            // ウィンドウの位置を復元したら最小化を解除
            WindowPresentation.State.Value = WindowState.Normal;

            // モデルのイベントハンドラを登録
            model.ChatCollectServiceModule.ErrorOccurredInChatCollecting += Model_ErrorOccurredInChatCollecting;
            model.ChatCollectServiceModule.ErrorOccurredInChatPosting += Model_ErrorOccurredInChatPosting;
            model.ChatCollectServiceModule.ErrorOccurredInServiceCreation += Model_ErrorOccurredAtChatCollectServiceCreation;

            // モデルのプロパティを結びつける
            CurrentPlayTime = model.ChannelInformationModule.CurrentTime;
            CurrentChannel = model.ChannelInformationModule.CurrentChannel;
            CurrentEvent = model.ChannelInformationModule.CurrentEvent;

            disposables.Add(CurrentPlayTime.Subscribe(_ => UpdateWindowTitle()));
            disposables.Add(CurrentChannel.Subscribe(_ => UpdateWindowTitle()));
            disposables.Add(CurrentEvent.Subscribe(_ => UpdateWindowTitle()));

            //da
            model.ChatTrendServiceModule.AddService(model.ChatServices.OfType<Model.ChatService.NiconicoChatService>().Single().ChatTrendServiceEntries[0]);
            forceValueData = new ReadOnlyObservableValue<Model.IForceValueData>(Observable.FromEventPattern<Model.ChatTrendServiceModule.ForceValueUpdatedEventArgs>(
                        h => model.ChatTrendServiceModule.ForceValueUpdated += h, h => model.ChatTrendServiceModule.ForceValueUpdated -= h).Select(y => y.EventArgs.ForceValueData));

            var presets = model.ChatCollectServiceCreationPresetModule.CreationPresets.MakeOneWayLinkedCollection(
                x => new ShellContents.ChatCollectServiceAddListItemViewModel(x.ServiceEntry, x));
            ChatCollectServiceAddList = presets.ObservableConcat(model.ChatServices.SelectMany(x => x.ChatCollectServiceEntries)
                .Select(x => new ShellContents.ChatCollectServiceAddListItemViewModel(x, null)));
            ChatCollectServices = model.ChatCollectServiceModule.RegisteredServices.MakeOneWayLinkedCollection(
                x => new ShellContents.ChatCollectServiceViewModel(x, updateTimer.Select(_ => new System.Reactive.Unit())));
            Channels = model.ChannelInformationModule.ChannelList.MakeOneWayLinkedCollectionMany(x =>
                x.Hidden ? null : new[] { new ShellContents.ChannelListItemViewModel(x, CurrentChannel, forceValueData) });
            disposables.Add(presets);
            disposables.Add(ChatCollectServiceAddList);
            disposables.Add(ChatCollectServices);
            disposables.Add(Channels);
            disposables.Add(forceValueData);

            UseDefaultChatCollectService = model.DefaultChatCollectServiceModule.IsEnabled;

            model.CommandModule.ShowWindowCommandInvoked += CommandModule_ShowWindowCommandInvoked;

            // コマンド生成
            CloseApplicationCommand = new RelayCommand(() =>
            {
                CloseApplication();
            });

            ChangeChannelCommand = new RelayCommand<Model.ChannelInfo>(channel => { if (channel != null) model.ChannelInformationModule.SetCurrentChannel(channel); });

            AddChatCollectServiceCommand = new RelayCommand<ShellContents.ChatCollectServiceAddListItemViewModel>(
                async x => await AddChatCollectService(x),
                _ => !UseDefaultChatCollectService.Value && !addingChatCollectService
            );
            RemoveChatCollectServiceCommand = new RelayCommand<Model.ChatCollectService.IChatCollectService>(
                service => { if (service != null) model.ChatCollectServiceModule.RemoveService(service); },
                _ => !UseDefaultChatCollectService.Value
            );
            UseDefaultChatCollectService.Subscribe(x =>
            {
                AddChatCollectServiceCommand.NotifyCanExecuteChanged();
                RemoveChatCollectServiceCommand.NotifyCanExecuteChanged();
            });

            ClearChatsCommand = new RelayCommand(() => model.ChatModule.ClearChats());
            AddWordNgCommand = new RelayCommand<Model.Chat>(chat =>
            {
                if (chat == null) return;
                model.ChatModule.AddChatModRule(new Model.ChatModRules.WordNgChatModRule(model.ChatServices.SelectMany(x => x.ChatCollectServiceEntries), chat.Text));
            });
            AddUserNgCommand = new RelayCommand<Model.Chat>(chat =>
            {
                if (chat?.SourceService?.Owner == null || chat.IsSystemMsg) return;
                model.ChatModule.AddChatModRule(new Model.ChatModRules.UserNgChatModRule(chat.SourceService.Owner.ChatCollectServiceEntries, chat.UserId));
            }, chat => chat?.SourceService?.Owner != null && !chat.IsSystemMsg);
            CopyCommentCommand = new RelayCommand<Model.Chat>(chat =>
            {
                if (chat == null) return;
                Clipboard.SetText(chat.Text);
            });
            CopyUserCommand = new RelayCommand<Model.Chat>(chat =>
            {
                if (chat == null) return;
                Clipboard.SetText(chat.UserId);
            });

            UpdateHoverdUserCommand = new RelayCommand<string>(userid =>
            {
                if (userid == null) return;
                model.ChatModule.SetHoverdUser(userid);
            });

            OnPropertyChanged((string)null);

            panelBridge = new TVTestPanelBridge(model.IpcModule, this);
            panelBridge.Start();

            if (!disposed)
            {
                niconico = model.ChatServices.OfType<Model.ChatService.NiconicoChatService>().Single();
                niconico.AuthenticationChanged += NiconicoAuthenticationChanged;
                // 起動時の復元で検出した失効の、メイン画面準備後の案内
                await niconico.ReloginIfExpiredAsync();
            }
        }

        private async void NiconicoAuthenticationChanged(object sender, EventArgs e)
        {
            if (!disposed) await niconico.ReloginIfExpiredAsync();
        }

        private void CloseApplication()
        {
            Window window = Application.Current.MainWindow;
            //最小化時は最小化前のBoundsを保存
            if (window.WindowState == WindowState.Minimized)
            {
                WindowPosition.Width = window.RestoreBounds.Width;
                WindowPosition.Height = window.RestoreBounds.Height;
            }
            window.Close();
        }

        private void UpdateWindowTitle()
        {
            var time = model.ChatModule.UiFlashingDeterrence.Value ? "" : CurrentPlayTime.Value?.ToString("yy/M/d(ddd) HH:mm:ss");
            WindowTitle.Value = $"{CurrentChannel.Value?.ServiceName} {time} - TVTComment";
        }

        internal async Task AddChatCollectService(ShellContents.ChatCollectServiceAddListItemViewModel item, bool useDefaultCreationOption = false)
        {
            if (item == null || disposed || !AddChatCollectServiceCommand.CanExecute(item)) return;
            addingChatCollectService = true;
            AddChatCollectServiceCommand.NotifyCanExecuteChanged();
            try { await AddChatCollectServiceCore(item, useDefaultCreationOption); }
            finally
            {
                addingChatCollectService = false;
                AddChatCollectServiceCommand.NotifyCanExecuteChanged();
            }
        }

        private async Task AddChatCollectServiceCore(ShellContents.ChatCollectServiceAddListItemViewModel item, bool useDefaultCreationOption)
        {
            if (item.IsPreset)
            {
                model.ChatCollectServiceModule.AddService(item.ServiceEntry, item.Preset.CreationOption);
            }
            else if (useDefaultCreationOption && item.ServiceEntry.CanUseDefaultCreationOption)
            {
                // TVTestパネルでは自動選択と同じ既定の作成オプションを使う
                model.ChatCollectServiceModule.AddService(item.ServiceEntry, null);
            }
            else
            {
                switch (item.ServiceEntry)
                {
                    case Model.ChatCollectServiceEntry.NiconicoChatCollectServiceEntry _:
                    case Model.ChatCollectServiceEntry.NiconicoLogChatCollectServiceEntry _:
                    case Model.ChatCollectServiceEntry.NewNiconicoJikkyouChatCollectServiceEntry _:
                    case Model.ChatCollectServiceEntry.PastNichanChatCollectServiceEntry _:
                    case Model.ChatCollectServiceEntry.TsukumijimaJikkyoApiChatCollectServiceEntry _:
                    case Model.ChatCollectServiceEntry.NxJikkyoChatCollectServiceEntry _:
                        model.ChatCollectServiceModule.AddService(item.ServiceEntry, null);
                        break;
                    case Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry _:
                    case Model.ChatCollectServiceEntry.FileChatCollectServiceEntry _:
                    case Model.ChatCollectServiceEntry.NiconicoLiveChatCollectServiceEntry _:
                    case Model.ChatCollectServiceEntry.TwitterLiveChatCollectServiceEntry _:
                    case Model.ChatCollectServiceEntry.TwitterLiveV2ChatCollectServiceEntry _:
                        // ダイアログを開く前にウィンドウの最小化を解除
                        WindowPresentation.ShowCommand.Execute(null);
                        var confirmation = await dialogService.ShowAsync(
                            new Notifications.ChatCollectServiceCreationSettingsConfirmation { Title = "コメント元設定", TargetChatCollectServiceEntry = item.ServiceEntry }
                        );
                        if (!confirmation.Confirmed || disposed || UseDefaultChatCollectService.Value) return;
                        model.ChatCollectServiceModule.AddService(item.ServiceEntry, confirmation.ChatCollectServiceCreationOption);
                        break;
                    default:
                        throw new Exception("Unknown ChatCollectServiceEntry: " + item.ServiceEntry.GetType().ToString());
                }
            }
        }

        private void Model_ErrorOccurredInChatCollecting(Model.ChatCollectService.IChatCollectService service, string errorText)
        {
            // 「自動選択」が有効
            if (UseDefaultChatCollectService.Value)
            {
                AddChatCollectServiceCommand.NotifyCanExecuteChanged();
                return;
            }

            dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = $"\"{service.Name}\"で以下のエラーが発生しました。このコメント元を無効化します。\n\n{errorText}" });
        }

        private void Model_ErrorOccurredInChatPosting(Model.ChatCollectService.IChatCollectService service, Model.ChatCollectService.BasicChatPostObject postObject, string errorText)
        {
            WindowPresentation.ShowCommand.Execute(null);
            dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = $"\"{service.Name}\"にコメントを投稿するとき以下のエラーが発生しました。\n\n{errorText}" });
        }

        private void Model_ErrorOccurredAtChatCollectServiceCreation(Model.ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, string errorText)
        {
            dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = $"コメント元、\"{serviceEntry.Name}\"を有効にしようとしたとき以下のエラーが発生し、有効化できませんでした。\n\n{errorText}" });
        }

        private void CommandModule_ShowWindowCommandInvoked()
        {
            WindowPresentation.ShowCommand.Execute(null);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            if (niconico != null)
                niconico.AuthenticationChanged -= NiconicoAuthenticationChanged;

            if (model.CommandModule != null)
                model.CommandModule.ShowWindowCommandInvoked -= CommandModule_ShowWindowCommandInvoked;
            if (model.ChatCollectServiceModule != null)
            {
                model.ChatCollectServiceModule.ErrorOccurredInServiceCreation -= Model_ErrorOccurredAtChatCollectServiceCreation;
                model.ChatCollectServiceModule.ErrorOccurredInChatPosting -= Model_ErrorOccurredInChatPosting;
                model.ChatCollectServiceModule.ErrorOccurredInChatCollecting -= Model_ErrorOccurredInChatCollecting;
            }

            if (model.Settings != null)
            {
                model.Settings.View.MainWindowPosition = new Model.Serialization.WindowPositionEntity()
                {
                    X = WindowPosition.X,
                    Y = WindowPosition.Y,
                    Width = WindowPosition.Width,
                    Height = WindowPosition.Height,
                };

                model.Settings.View.ChatListViewColumns = ChatListColumnInfos?.Select(
                    x => new ListViewColumnViewModel { Id = x.Id, Width = x.Width }
                ).ToArray();

                model.Settings.View.MainWindowTab = SelectedTab.Value;
            }

            panelBridge?.Dispose();
            BasicSettingControlViewModel?.Dispose();
            ChatPostControlViewModel?.Dispose();
            model.Dispose();
            model.ApplicationClose -= CloseApplication;
            disposables.Dispose();

            disposed = true;
        }
    }
}
