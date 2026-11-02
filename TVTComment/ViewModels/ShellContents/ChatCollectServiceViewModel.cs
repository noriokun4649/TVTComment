using System;
using System.Reactive;

namespace TVTComment.ViewModels.ShellContents
{
    class ChatCollectServiceViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject, IDisposable
    {
        private readonly IDisposable disposable;
        private string informationText;

        public Model.ChatCollectService.IChatCollectService Service { get; }
        public string InformationText
        {
            get { return informationText; }
            set { SetProperty(ref informationText, value); }
        }

        public ChatCollectServiceViewModel(Model.ChatCollectService.IChatCollectService service, IObservable<Unit> update)
        {
            Service = service;
            InformationText = service.GetInformationText();

            disposable = update.Subscribe(_ =>
            {
                InformationText = service.GetInformationText();
            });
        }

        public void Dispose()
        {
            disposable.Dispose();
        }
    }
}
