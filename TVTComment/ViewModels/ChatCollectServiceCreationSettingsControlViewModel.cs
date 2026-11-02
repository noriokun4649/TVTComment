using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TVTComment.ViewModels.Services;
using System;
using System.Windows.Input;
using TVTComment.ViewModels.ChatCollectServiceCreationOptionControl;

namespace TVTComment.ViewModels
{
    class ChatCollectServiceCreationSettingsControlViewModel : ObservableObject, IDialogViewModel
    {
        private readonly ChatCollectServiceCreationOptionFactory optionFactory;

        private Notifications.ChatCollectServiceCreationSettingsConfirmation confirmation;
        public event Action CloseRequested;

        private ChatCollectServiceCreationOptionControlViewModel creationOptionRegionViewModel;
        public ChatCollectServiceCreationOptionControlViewModel CreationOptionRegionViewModel
        {
            get { return creationOptionRegionViewModel; }
            set { RegionChanged(creationOptionRegionViewModel, value); SetProperty(ref creationOptionRegionViewModel, value); }
        }

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public ChatCollectServiceCreationSettingsControlViewModel(ChatCollectServiceCreationOptionFactory optionFactory)
        {
            this.optionFactory = optionFactory;

            OkCommand = new RelayCommand(() =>
            {
                var option = CreationOptionRegionViewModel.GetChatCollectServiceCreationOption();
                if (option == null) return;
                confirmation.ChatCollectServiceCreationOption = option;
                confirmation.Confirmed = true;
                CloseRequested?.Invoke();
            });
            CancelCommand = new RelayCommand(() =>
            {
                CloseRequested?.Invoke();
            });
        }

        public void Initialize(Notifications.ChatCollectServiceCreationSettingsConfirmation confirmation)
        {
            this.confirmation = confirmation;
            confirmation.Confirmed = false;
            confirmation.ChatCollectServiceCreationOption = null;

            string creationOptionControl = confirmation.TargetChatCollectServiceEntry switch
            {
                Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry _ => "NichanChatCollectServiceCreationOptionControl",
                Model.ChatCollectServiceEntry.FileChatCollectServiceEntry _ => "FileChatCollectServiceCreationOptionControl",
                Model.ChatCollectServiceEntry.NiconicoLiveChatCollectServiceEntry _ => "NiconicoLiveChatCollectServiceCreationOptionControl",
                Model.ChatCollectServiceEntry.TwitterLiveChatCollectServiceEntry _ => "TwitterLiveChatCollectServiceCreationOptionControl",
                Model.ChatCollectServiceEntry.TwitterLiveV2ChatCollectServiceEntry _ => "TwitterLiveChatCollectServiceCreationOptionControl",
                _ => throw new Exception($"Unknown TargetChatCollectServiceEntry: {confirmation.TargetChatCollectServiceEntry}"),
            };
            CreationOptionRegionViewModel = optionFactory.GetViewModel(creationOptionControl);
        }

        private void RegionChanged(ChatCollectServiceCreationOptionControlViewModel oldViewModel, ChatCollectServiceCreationOptionControlViewModel newViewModel)
        {
            if (oldViewModel != null)
                oldViewModel.Finished -= ContentViewModel_Finished;
            if (newViewModel != null)
                newViewModel.Finished += ContentViewModel_Finished;
        }

        private void ContentViewModel_Finished(object sender, EventArgs e)
        {
            OkCommand.Execute(null);
        }
    }
}
