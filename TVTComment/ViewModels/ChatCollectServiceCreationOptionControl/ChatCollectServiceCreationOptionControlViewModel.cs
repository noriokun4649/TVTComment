using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TVTComment.ViewModels.ChatCollectServiceCreationOptionControl
{
    abstract class ChatCollectServiceCreationOptionControlViewModel : ObservableObject
    {
        public abstract Model.ChatCollectServiceEntry.IChatCollectServiceCreationOption GetChatCollectServiceCreationOption();
        public abstract event EventHandler Finished;
    }
}
