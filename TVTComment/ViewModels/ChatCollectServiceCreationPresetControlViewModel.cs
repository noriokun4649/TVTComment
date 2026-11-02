using ObservableUtils;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TVTComment.ViewModels.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using TVTComment.ViewModels.Contents;

namespace TVTComment.ViewModels
{
    class ChatCollectServiceCreationPresetControlViewModel : ObservableObject, IDialogViewModel
    {
        private readonly ChatCollectServiceCreationOptionFactory optionFactory;
        private SettingsWindowContents.ChatCollectServiceCreationPresetConfirmation confirmation;

        public IReadOnlyList<SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>> ChatCollectServiceEntries { get; }
        public ObservableValue<string> PresetName { get; } = new ObservableValue<string>();
        private ChatCollectServiceCreationOptionControl.ChatCollectServiceCreationOptionControlViewModel optionRegionViewModel;
        public ChatCollectServiceCreationOptionControl.ChatCollectServiceCreationOptionControlViewModel OptionRegionViewModel
        {
            get { return optionRegionViewModel; }
            set { SetProperty(ref optionRegionViewModel, value); }
        }

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action CloseRequested;

        public ChatCollectServiceCreationPresetControlViewModel(ChatCollectServiceCreationOptionFactory optionFactory, Model.TVTComment model)
        {
            this.optionFactory = optionFactory;
            ChatCollectServiceEntries = model.ChatServices.SelectMany(x => x.ChatCollectServiceEntries).Select(x => new SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>(x)).ToList();
            foreach (var entry in ChatCollectServiceEntries)
                entry.PropertyChanged += ChatCollectServiceEntryListItemChanged;
            ChatCollectServiceEntries[0].IsSelected = true;

            OkCommand = new RelayCommand(() =>
              {
                  var entry = ChatCollectServiceEntries.FirstOrDefault(x => x.IsSelected)?.Value;
                  var option = OptionRegionViewModel?.GetChatCollectServiceCreationOption();
                  if (string.IsNullOrWhiteSpace(PresetName.Value) || entry == null || option == null) return;
                  confirmation.ChatCollectServiceCreationPreset = new Model.ChatCollectServiceCreationPreset(PresetName.Value, entry, option);
                  confirmation.Confirmed = true;
                  CloseRequested?.Invoke();
              });
            CancelCommand = new RelayCommand(() => { confirmation.Confirmed = false; CloseRequested?.Invoke(); });
        }

        public void Initialize(SettingsWindowContents.ChatCollectServiceCreationPresetConfirmation confirmation)
        {
            this.confirmation = confirmation;
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
            if (!string.IsNullOrEmpty(creationOptionControl))
                OptionRegionViewModel = optionFactory.GetViewModel(creationOptionControl);
        }
    }
}
