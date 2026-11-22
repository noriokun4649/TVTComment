using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Reactive.Disposables;

namespace TVTComment.ViewModels.ShellContents
{
    class ChannelListItemViewModel : ObservableObject, IDisposable
    {
        public Model.ChannelInfo Channel { get; }

        private string forceValue;
        public string ForceValue
        {
            get { return forceValue; }
            set { SetProperty(ref forceValue, value); }
        }

        private bool watching;
        public bool Watching
        {
            get { return watching; }
            set { SetProperty(ref watching, value); }
        }

        private readonly CompositeDisposable disposables = new CompositeDisposable();

        public ChannelListItemViewModel(Model.ChannelInfo channel, IObservable<Model.ChannelInfo> currentChannel, IObservable<Model.IForceValueData> forceValueData)
        {
            Channel = channel;
            disposables.Add(currentChannel.Subscribe(current =>
            {
                Watching = current == channel;
            }));
            disposables.Add(forceValueData.Subscribe(forceValues =>
            {
                ForceValue = forceValues == Model.ChatTrendServiceModule.FailedUpdate
                    ? "E" : forceValues?.GetForceValue(channel)?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }));
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
