using ObservableUtils;
using Prism.Commands;
using Prism.Interactivity.InteractionRequest;
using Prism.Mvvm;
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
    class ShellViewModel : BindableBase, IDisposable
    {
        internal class Rect : BindableBase
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

        public DisposableReadOnlyObservableCollection<ShellContents.ChatCollectServiceAddListItemViewModel> ChatCollectServiceAddList =>
            model.ChatCollectServiceCreationPresetModule?.CreationPresets.MakeOneWayLinkedCollection(x => new ShellContents.ChatCollectServiceAddListItemViewModel(x.ServiceEntry, x))
            .ObservableConcat(model.ChatServices.SelectMany(x => x.ChatCollectServiceEntries).Select(x => new ShellContents.ChatCollectServiceAddListItemViewModel(x, null)));

        private readonly IObservable<long> updateTimer = Observable.Interval(new TimeSpan(0, 0, 5), new SynchronizationContextScheduler(SynchronizationContext.Current));
        public DisposableReadOnlyObservableCollection<ShellContents.ChatCollectServiceViewModel> ChatCollectServices =>
            model.ChatCollectServiceModule?.RegisteredServices?.MakeOneWayLinkedCollection(x => new ShellContents.ChatCollectServiceViewModel(x, updateTimer.Select(_ => new System.Reactive.Unit())));

        public ReadOnlyObservableCollection<Model.Chat> Chats => model.ChatModule?.Chats;

        private ReadOnlyObservableValue<Model.IForceValueData> forceValueData;
        public DisposableReadOnlyObservableCollection<ShellContents.ChannelListItemViewModel> Channels =>
            model.ChannelInformationModule?.ChannelList?.MakeOneWayLinkedCollectionMany(x =>
            {
                if (x.Hidden) return null;
                return new[] { new ShellContents.ChannelListItemViewModel(x, model.ChannelInformationModule.CurrentChannel, forceValueData) };
            });

        public ReadOnlyObservableValue<DateTime?> CurrentPlayTime { get; private set; }
        public ReadOnlyObservableValue<Model.ChannelInfo> CurrentChannel { get; private set; }
        public ReadOnlyObservableValue<Model.EventInfo> CurrentEvent { get; private set; }
        public ObservableValue<bool> UseDefaultChatCollectService { get; private set; }

        public ObservableValue<string> WindowTitle { get; } = new ObservableValue<string>("TVTComment");
        public ObservableValue<double> WindowOpacity => BasicSettingControlViewModel?.WindowOpacity;
        public ObservableValue<bool> WindowTopmost => BasicSettingControlViewModel?.WindowTopmost;
        public ObservableValue<double> WindowFontSize => BasicSettingControlViewModel?.WindowFontSize;
        public ObservableValue<MainWindowTab> SelectedTab { get; } = new ObservableValue<MainWindowTab>();

        public Views.AttachedProperties.GridViewColumnSettingsBinder.ColumnInfo[] ChatListColumnInfos { set; get; } = Array.Empty<Views.AttachedProperties.GridViewColumnSettingsBinder.ColumnInfo>();

        public ICommand CloseApplicationCommand { get; private set; }
        public ICommand MinimizeWindowCommand { get; private set; }
        public ICommand ChangeChannelCommand { get; private set; }
        public DelegateCommand<ShellContents.ChatCollectServiceAddListItemViewModel> AddChatCollectServiceCommand { get; private set; }
        public DelegateCommand<Model.ChatCollectService.IChatCollectService> RemoveChatCollectServiceCommand { get; private set; }
        public ICommand ClearChatsCommand { get; private set; }
        public ICommand AddWordNgCommand { get; private set; }
        public ICommand AddUserNgCommand { get; private set; }
        public ICommand CopyCommentCommand { get; private set; }
        public ICommand CopyUserCommand { get; private set; }

        public InteractionRequest<Notification> AlertRequest { get; } = new InteractionRequest<Notification>();
        public InteractionRequest<Notifications.ChatCollectServiceCreationSettingsConfirmation> ChatCollectServiceCreationSettingsRequest { get; } = new InteractionRequest<Notifications.ChatCollectServiceCreationSettingsConfirmation>();

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
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly Model.TVTComment model;

        public ShellViewModel(Model.TVTComment model)
        {
            this.model = model;

            Window mainWindow = Application.Current.MainWindow;
            mainWindow.MouseLeftButtonDown += (_, __) => { mainWindow.DragMove(); };
            // 初期化が終わるまで最小化しておく
            mainWindow.WindowState = WindowState.Minimized;

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
                //await ChatPostControlViewModel.Initialize();
            }
            catch (Exception e)
            {
                AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = $"初期化で予期しないエラーが発生しました\n{e}" });
                CloseApplication();
                return;
            }
            if (model.State != Model.TVTCommentState.Working)
            {
                AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = "Viewer側（TVTestプラグイン側）と接続できませんでした" });
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
            Window window = Application.Current.MainWindow;
            window.WindowState = WindowState.Normal;

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

            UseDefaultChatCollectService = model.DefaultChatCollectServiceModule.IsEnabled;

            model.CommandModule.ShowWindowCommandInvoked += CommandModule_ShowWindowCommandInvoked;

            // コマンド生成
            CloseApplicationCommand = new DelegateCommand(() =>
            {
                CloseApplication();
            });

            MinimizeWindowCommand = new DelegateCommand(() =>
            {
                window.WindowState = WindowState.Minimized;
            });

            ChangeChannelCommand = new DelegateCommand<Model.ChannelInfo>(channel => { if (channel != null) model.ChannelInformationModule.SetCurrentChannel(channel); });

            AddChatCollectServiceCommand = new DelegateCommand<ShellContents.ChatCollectServiceAddListItemViewModel>(
                async x => { if (x != null) await AddChatCollectService(x); },
                _ => !UseDefaultChatCollectService.Value
            );
            RemoveChatCollectServiceCommand = new DelegateCommand<Model.ChatCollectService.IChatCollectService>(
                service => { if (service != null) model.ChatCollectServiceModule.RemoveService(service); },
                _ => !UseDefaultChatCollectService.Value
            );
            UseDefaultChatCollectService.Subscribe(x =>
            {
                AddChatCollectServiceCommand.RaiseCanExecuteChanged();
                RemoveChatCollectServiceCommand.RaiseCanExecuteChanged();
            });

            ClearChatsCommand = new DelegateCommand(() => model.ChatModule.ClearChats());
            AddWordNgCommand = new DelegateCommand<Model.Chat>(chat =>
            {
                if (chat == null) return;
                model.ChatModule.AddChatModRule(new Model.ChatModRules.WordNgChatModRule(model.ChatServices.SelectMany(x => x.ChatCollectServiceEntries), chat.Text));
            });
            AddUserNgCommand = new DelegateCommand<Model.Chat>(chat =>
            {
                if (chat == null) return;
                model.ChatModule.AddChatModRule(new Model.ChatModRules.UserNgChatModRule(chat.SourceService.Owner.ChatCollectServiceEntries, chat.UserId));
            });
            CopyCommentCommand = new DelegateCommand<Model.Chat>(chat =>
            {
                if (chat == null) return;
                Clipboard.SetText(chat.Text);
            });
            CopyUserCommand = new DelegateCommand<Model.Chat>(chat =>
            {
                if (chat == null) return;
                Clipboard.SetText(chat.UserId);
            });

            OnPropertyChanged(null);
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

        private async Task AddChatCollectService(ShellContents.ChatCollectServiceAddListItemViewModel item)
        {
            if (item.IsPreset)
            {
                model.ChatCollectServiceModule.AddService(item.ServiceEntry, item.Preset.CreationOption);
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
                        var confirmation = await ChatCollectServiceCreationSettingsRequest.RaiseAsync(
                            new Notifications.ChatCollectServiceCreationSettingsConfirmation { Title = "コメント元設定", TargetChatCollectServiceEntry = item.ServiceEntry }
                        );
                        if (!confirmation.Confirmed) return;
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
                AddChatCollectServiceCommand.RaiseCanExecuteChanged();
                return;
            }

            AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = $"\"{service.Name}\"で以下のエラーが発生しました。このコメント元を無効化します。\n\n{errorText}" });
        }

        private void Model_ErrorOccurredInChatPosting(Model.ChatCollectService.IChatCollectService service, Model.ChatCollectService.BasicChatPostObject postObject, string errorText)
        {
            AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = $"\"{service.Name}\"にコメントを投稿するとき以下のエラーが発生しました。\n\n{errorText}" });
        }

        private void Model_ErrorOccurredAtChatCollectServiceCreation(Model.ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, string errorText)
        {
            AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = $"コメント元、\"{serviceEntry.Name}\"を有効にしようとしたとき以下のエラーが発生し、有効化できませんでした。\n\n{errorText}" });
        }

        private void CommandModule_ShowWindowCommandInvoked()
        {
            Window window = Application.Current.MainWindow;
            //windowをアクティブにする。window.Activate()は不確実。実験した中ではこれが一番確実だった
            window.WindowState = WindowState.Minimized;
            window.WindowState = WindowState.Normal;
        }

        public void Dispose()
        {
            if (disposed)
                return;

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

            model.Dispose();
            model.ApplicationClose -= CloseApplication;
            disposables.Dispose();

            disposed = true;
        }
    }
}
