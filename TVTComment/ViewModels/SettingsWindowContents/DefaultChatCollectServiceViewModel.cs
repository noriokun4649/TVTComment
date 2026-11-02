using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace TVTComment.ViewModels.ShellContents
{
    class DefaultChatCollectServiceViewModel : ObservableObject
    {
        private readonly IList<Model.ChatCollectServiceEntry.IChatCollectServiceEntry> enabledServiceEntries;

        private bool isEnabled;
        public bool IsEnabled
        {
            get { return isEnabled; }
            set { SetProperty(ref isEnabled, value); Update(); }
        }

        public Model.ChatCollectServiceEntry.IChatCollectServiceEntry ServiceEntry { get; }

        public DefaultChatCollectServiceViewModel(IList<Model.ChatCollectServiceEntry.IChatCollectServiceEntry> enableChatCollectServiceEntry, Model.ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, bool isEnabled)
        {
            this.isEnabled = isEnabled;
            enabledServiceEntries = enableChatCollectServiceEntry;
            ServiceEntry = serviceEntry;
            Update();
        }

        private void Update()
        {
            if (isEnabled)
            {
                if (enabledServiceEntries.Contains(ServiceEntry)) return;
                enabledServiceEntries.Add(ServiceEntry);
            }
            else
            {
                enabledServiceEntries.Remove(ServiceEntry);
            }
        }
    }
}
