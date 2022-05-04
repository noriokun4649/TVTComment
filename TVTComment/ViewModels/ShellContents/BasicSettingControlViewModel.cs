using ObservableUtils;
using Prism.Commands;
using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reflection;
using System.Threading.Tasks;

namespace TVTComment.ViewModels.ShellContents
{
    class BasicSettingControlViewModel : IDisposable, INotifyPropertyChanged
    {
        private readonly Model.TVTComment model;
        private readonly CompositeDisposable compositeDisposable = new CompositeDisposable();

        public event PropertyChangedEventHandler PropertyChanged;

        public string Version { get; }
        public ObservableValue<byte> ChatOpacity { get; private set; }
        public ObservableValue<double> WindowOpacity { get; } = new ObservableValue<double>(1.0);
        public ObservableValue<bool> WindowTopmost { get; } = new ObservableValue<bool>(false);
        public ObservableValue<TimeSpan> ChatCollectTimeAdjustment { get; private set; }
        public ObservableValue<bool> ClearChatsOnChannelChange { get; private set; }
        public ObservableValue<bool> UiFlashingDeterrence { get; private set; }
        public ObservableValue<double> WindowFontSize { get; } = new ObservableValue<double>(1.2);

        public DelegateCommand<int?> SetChatCollectTimeAdjustment { get; private set; }
        public DelegateCommand<int?> AddChatCollectTimeAdjustment { get; private set; }

        public BasicSettingControlViewModel(Model.TVTComment model)
        {
            this.model = model;

            var assembly = Assembly.GetExecutingAssembly().GetName();
            Version = assembly.Version.ToString(4);
        }

        public async Task Initialize()
        {
            await model.Initialize();
            //modelの初期化エラーへの対処はShellViewModelでするので全部無視    
            if (model.State != Model.TVTCommentState.Working) return;

            WindowTopmost.Value = model.Settings.View.WindowTopmost;
            WindowOpacity.Value = model.Settings.View.WindowOpacity;
            WindowFontSize.Value = model.Settings.View.WindowFontSize;

            compositeDisposable.Add(WindowTopmost.Subscribe(x => model.Settings.View.WindowTopmost = x));
            compositeDisposable.Add(WindowOpacity.Subscribe(x => model.Settings.View.WindowOpacity = x));
            compositeDisposable.Add(WindowFontSize.Subscribe(x => model.Settings.View.WindowFontSize = x));

            //256段階でスライダーを動かすと大量にSetChatOpacityIPCMessageが発生してしまうため16段階にする
            ChatOpacity = model.ChatOpacity.MakeLinkedObservableValue(x => (byte)(x / 16), x => (byte)(x * 16));
            ClearChatsOnChannelChange = model.ChatModule.ClearChatsOnChannelChange;
            UiFlashingDeterrence = model.ChatModule.UiFlashingDeterrence;

            ChatCollectTimeAdjustment = model.ChatCollectServiceModule.TimeAdjustment;

            AddChatCollectTimeAdjustment = new DelegateCommand<int?>(time => { if (time.HasValue) ChatCollectTimeAdjustment.Value = ChatCollectTimeAdjustment.Value.Add(TimeSpan.FromSeconds(time.Value)); }, _ => true);
            SetChatCollectTimeAdjustment = new DelegateCommand<int?>(time => { if (time.HasValue) ChatCollectTimeAdjustment.Value = TimeSpan.FromSeconds(time.Value); }, _ => true);

            PropertyChanged(this, new PropertyChangedEventArgs(null));
        }

        public void Dispose()
        {
            // アプリケーションが終了するとき（それ以外ないが）は現状呼ばれない
            compositeDisposable.Dispose();
        }
    }
}
