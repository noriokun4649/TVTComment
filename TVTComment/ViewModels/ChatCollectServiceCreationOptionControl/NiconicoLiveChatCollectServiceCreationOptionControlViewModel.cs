using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows.Input;
using TVTComment.Model.ChatCollectServiceEntry;

namespace TVTComment.ViewModels.ChatCollectServiceCreationOptionControl
{
    class NiconicoLiveChatCollectServiceCreationOptionControlViewModel : ChatCollectServiceCreationOptionControlViewModel
    {
        private string liveId;

        public string LiveId
        {
            get { return liveId; }
            set { SetProperty(ref liveId, value); }
        }

        public ICommand OkCommand { get; }

        public override event EventHandler Finished;

        public NiconicoLiveChatCollectServiceCreationOptionControlViewModel()
        {
            OkCommand = new RelayCommand(() => Finished(this, new EventArgs()));
        }

        public override IChatCollectServiceCreationOption GetChatCollectServiceCreationOption()
        {
            if (string.IsNullOrWhiteSpace(LiveId))
                return null;
            return new NiconicoLiveChatCollectServiceEntry.ChatCollectServiceCreationOption(LiveId);
        }
    }
}
