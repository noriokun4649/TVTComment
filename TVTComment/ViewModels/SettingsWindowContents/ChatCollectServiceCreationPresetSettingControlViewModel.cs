using CommunityToolkit.Mvvm.Input;
using TVTComment.ViewModels.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace TVTComment.ViewModels.SettingsWindowContents
{
    class ChatCollectServiceCreationPresetSettingControlViewModel
    {
        public Model.ChatCollectServiceCreationPreset SelectedChatCollectServiceCreationPreset { get; set; }
        public ReadOnlyObservableCollection<Model.ChatCollectServiceCreationPreset> ChatCollectServiceCreationPresets { get; }

        public ICommand AddPreset { get; }
        public ICommand RemovePreset { get; }

        public ChatCollectServiceCreationPresetSettingControlViewModel(Model.TVTComment model, IDialogService dialogService)
        {
            ChatCollectServiceCreationPresets = model.ChatCollectServiceCreationPresetModule.CreationPresets;

            AddPreset = new RelayCommand(async () =>
              {
                  ChatCollectServiceCreationPresetConfirmation result = await dialogService.ShowAsync(new ChatCollectServiceCreationPresetConfirmation { Title = "プリセット追加" });
                  if (!result.Confirmed) return;
                  model.ChatCollectServiceCreationPresetModule.AddCreationPreset(
                      result.ChatCollectServiceCreationPreset.Name, result.ChatCollectServiceCreationPreset.ServiceEntry, result.ChatCollectServiceCreationPreset.CreationOption);
              });
            RemovePreset = new RelayCommand(() =>
              {
                  model.ChatCollectServiceCreationPresetModule.RemoveCreationPreset(SelectedChatCollectServiceCreationPreset);
              });
        }
    }
}
