using Prism.Commands;
using System;
using System.Windows.Input;
using TVTComment.Model.ChatCollectServiceEntry;
using static TVTComment.Model.ChatCollectServiceEntry.TwitterLiveChatCollectServiceEntry.ChatCollectServiceCreationOption;

namespace TVTComment.ViewModels.ChatCollectServiceCreationOptionControl
{
    class TwitterLiveChatCollectServiceCreationOptionControlViewModel : ChatCollectServiceCreationOptionControlViewModel
    {
        private string searchWord;

        public string SearchWord
        {
            get { return searchWord; }
            set { SetProperty(ref searchWord, value); }
        }

        public ICommand OkCommand { get; }

        private ModeSelectMethod method;
        public ModeSelectMethod Method
        {
            get { return method; }
            set { SetProperty(ref method, value); }
        }

        public override event EventHandler Finished;

        public TwitterLiveChatCollectServiceCreationOptionControlViewModel()
        {
            OkCommand = new DelegateCommand(() => Finished(this, new EventArgs()));
            Method = ModeSelectMethod.Preset;
        }

        public override IChatCollectServiceCreationOption GetChatCollectServiceCreationOption()
        {
            if (string.IsNullOrWhiteSpace(searchWord) && ModeSelectMethod.Manual == Method)
                return null;
            return new TwitterLiveChatCollectServiceEntry.ChatCollectServiceCreationOption(Method, searchWord);
        }
    }
}
