using Prism.Commands;
using Prism.Interactivity.InteractionRequest;
using Prism.Mvvm;
using Prism.Regions;
using System;
using System.Windows.Input;
using TVTComment.ViewModels.ChatCollectServiceCreationOptionControl;

namespace TVTComment.ViewModels
{
    class ChatCollectServiceCreationSettingsControlViewModel : BindableBase, IInteractionRequestAware
    {
        private readonly IRegionManager regionManager;

        private Notifications.ChatCollectServiceCreationSettingsConfirmation confirmation;
        public Action FinishInteraction { get; set; }
        public INotification Notification
        {
            get { return confirmation; }
            set { confirmation = (Notifications.ChatCollectServiceCreationSettingsConfirmation)value; Initialize(); }
        }

        private ChatCollectServiceCreationOptionControlViewModel creationOptionRegionViewModel;
        public ChatCollectServiceCreationOptionControlViewModel CreationOptionRegionViewModel
        {
            get { return creationOptionRegionViewModel; }
            set { RegionChanged(creationOptionRegionViewModel, value); creationOptionRegionViewModel = value; }
        }

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public ChatCollectServiceCreationSettingsControlViewModel(IRegionManager regionManager)
        {
            this.regionManager = regionManager;

            OkCommand = new DelegateCommand(() =>
            {
                var option = CreationOptionRegionViewModel.GetChatCollectServiceCreationOption();
                if (option == null) return;
                confirmation.ChatCollectServiceCreationOption = option;
                confirmation.Confirmed = true;
                FinishInteraction();
            });
            CancelCommand = new DelegateCommand(() =>
            {
                FinishInteraction();
            });
        }

        private void Initialize()
        {
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
            regionManager.RequestNavigate("ChatCollectServiceCreationSettingsControl_CreationOptionRegion", creationOptionControl);
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
