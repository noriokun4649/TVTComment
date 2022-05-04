using ObservableUtils;
using Prism.Commands;
using Prism.Interactivity.InteractionRequest;
using Prism.Regions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using TVTComment.ViewModels.Contents;

namespace TVTComment.ViewModels
{
    class ChatCollectServiceCreationPresetControlViewModel : IInteractionRequestAware
    {
        private readonly IRegionManager regionManager;
        private SettingsWindowContents.ChatCollectServiceCreationPresetConfirmation confirmation;

        public IReadOnlyList<SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>> ChatCollectServiceEntries { get; }
        public ObservableValue<string> PresetName { get; } = new ObservableValue<string>();
        public ChatCollectServiceCreationOptionControl.ChatCollectServiceCreationOptionControlViewModel OptionRegionViewModel { get; set; }

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public INotification Notification
        {
            get { return confirmation; }
            set { confirmation = (SettingsWindowContents.ChatCollectServiceCreationPresetConfirmation)value; Initialize(); }
        }

        public Action FinishInteraction { get; set; }

        public ChatCollectServiceCreationPresetControlViewModel(IRegionManager regionManager, Model.TVTComment model)
        {
            this.regionManager = regionManager;
            ChatCollectServiceEntries = model.ChatServices.SelectMany(x => x.ChatCollectServiceEntries).Select(x => new SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>(x)).ToList();
            foreach (var entry in ChatCollectServiceEntries)
                entry.PropertyChanged += ChatCollectServiceEntryListItemChanged;
            ChatCollectServiceEntries[0].IsSelected = true;

            OkCommand = new DelegateCommand(() =>
              {
                  var entry = ChatCollectServiceEntries.FirstOrDefault(x => x.IsSelected)?.Value;
                  var option = OptionRegionViewModel?.GetChatCollectServiceCreationOption();
                  if (string.IsNullOrWhiteSpace(PresetName.Value) || entry == null || option == null) return;
                  confirmation.ChatCollectServiceCreationPreset = new Model.ChatCollectServiceCreationPreset(PresetName.Value, entry, option);
                  confirmation.Confirmed = true;
                  FinishInteraction();
              });
            CancelCommand = new DelegateCommand(() => { confirmation.Confirmed = false; FinishInteraction(); });
        }

        private void Initialize()
        {
            confirmation.Confirmed = false;
            confirmation.ChatCollectServiceCreationPreset = null;
        }

        private void ChatCollectServiceEntryListItemChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>.IsSelected))
                ServiceEntryChanged();
        }

        private void ServiceEntryChanged()
        {
            var entry = ChatCollectServiceEntries.FirstOrDefault(x => x.IsSelected)?.Value;
            string creationOptionControl = entry switch
            {
                Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry _ => "NichanChatCollectServiceCreationOptionControl",
                Model.ChatCollectServiceEntry.FileChatCollectServiceEntry _ => "FileChatCollectServiceCreationOptionControl",
                Model.ChatCollectServiceEntry.NiconicoLiveChatCollectServiceEntry _ => "NiconicoLiveChatCollectServiceCreationOptionControl",
                Model.ChatCollectServiceEntry.TwitterLiveChatCollectServiceEntry _ => "TwitterLiveChatCollectServiceCreationOptionControl",
                Model.ChatCollectServiceEntry.TwitterLiveV2ChatCollectServiceEntry _ => "TwitterLiveChatCollectServiceCreationOptionControl",
                _ => "",
            };
            regionManager.RequestNavigate("ChatCollectServiceCreationSettingsControl_CreationOptionRegion", creationOptionControl);
        }
    }
}
