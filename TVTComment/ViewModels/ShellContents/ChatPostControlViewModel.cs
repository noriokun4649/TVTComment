using ObservableUtils;
using Prism.Commands;
using Prism.Interactivity.InteractionRequest;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace TVTComment.ViewModels.ShellContents
{
    class ChatPostControlViewModel : INotifyPropertyChanged
    {
        private readonly Model.TVTComment model;

        public ObservableValue<string> PostSplitterLeftSize => model.ChatPostSplitterLeftSize;
        public ObservableValue<string> PostSplitterRightSize => model.ChatPostSplitterRightSize;
        public ObservableValue<byte> ChatOpacity { get; private set; }
        public ObservableValue<string> PostText { get; } = new ObservableValue<string>("");
        public ObservableValue<string> PostMailText { get; } = new ObservableValue<string>("");
        public DisposableReadOnlyObservableCollection<Model.ChatCollectService.IChatCollectService> PostServices { get; private set; }
        public ObservableValue<Model.ChatCollectService.IChatCollectService> SelectedPostService { get; } = new ObservableValue<Model.ChatCollectService.IChatCollectService>();
        public ReadOnlyObservableValue<bool> IsShowingNiconicoPostForm { get; private set; }
        public ReadOnlyObservableValue<bool> IsShowingNichanPostForm { get; private set; }
        public ObservableCollection<Nichan.Thread> NichanCurrentThreads { get; } = new ObservableCollection<Nichan.Thread>();
        public ObservableValue<Nichan.Thread> SelectedNichanCurrentThread { get; } = new ObservableValue<Nichan.Thread>();
        public ObservableCollection<string> PostMailTextExamples => model.ChatPostMailTextExamples;

        public ICommand PostCommand { get; private set; }
        public ICommand UpdateNichanCurrentThreadsCommand { get; private set; }
        public ICommand AddPostMailTextExampleCommand { get; private set; }
        public ICommand RemovePostMailTextExampleCommand { get; private set; }

        public InteractionRequest<Notification> AlertRequest { get; } = new InteractionRequest<Notification>();

        public event PropertyChangedEventHandler PropertyChanged;

        public ChatPostControlViewModel(Model.TVTComment model)
        {
            this.model = model;
            this.model.Initialize().ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully) return;
                Initialize();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void Initialize()
        {
            ChatOpacity = model.ChatOpacity.MakeLinkedObservableValue(x => (byte)(x / 16), x => (byte)(x * 16));
            PostServices = model.ChatCollectServiceModule?.RegisteredServices?.MakeOneWayLinkedCollectionMany(x =>
            {
                if (x.CanPost) return new[] { x };
                else return Array.Empty<Model.ChatCollectService.IChatCollectService>();
            });
            IsShowingNiconicoPostForm = new ReadOnlyObservableValue<bool>(
                SelectedPostService.Select(x =>
                    x is Model.ChatCollectService.NiconicoChatCollectService ||
                    x is Model.ChatCollectService.NiconicoLiveChatCollectService ||
                    x is Model.ChatCollectService.NewNiconicoJikkyouChatCollectService ||
                    x is Model.ChatCollectService.TwitterLiveChatCollectService
                )
            );
            IsShowingNichanPostForm = new ReadOnlyObservableValue<bool>(
                SelectedPostService.Select(x => x is Model.ChatCollectService.NichanChatCollectService)
            );

            PostCommand = new DelegateCommand(PostChat);
            UpdateNichanCurrentThreadsCommand = new DelegateCommand(() =>
            {
                if (SelectedPostService.Value is not Model.ChatCollectService.NichanChatCollectService nichanChatCollectService)
                    return;
                NichanCurrentThreads.Clear();
                foreach (var thread in nichanChatCollectService.CurrentThreads)
                    NichanCurrentThreads.Add(thread);
            });
            AddPostMailTextExampleCommand = new DelegateCommand<string>(AddPostMailTextExample);
            RemovePostMailTextExampleCommand = new DelegateCommand<string>(RemovePostMailTextExample);

            PropertyChanged(this, new PropertyChangedEventArgs(null));
        }

        private void PostChat()
        {
            if (SelectedPostService.Value == null)
            {
                AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = "コメントの投稿先が選択されていません" });
                return;
            }

            string mail184 = PostMailText.Value;
            if (model.Settings.Niconico.Always184)
            {
                if (mail184 == "")
                    mail184 = "184";
                else
                    mail184 += " 184";
            }
            if (SelectedPostService.Value is Model.ChatCollectService.NiconicoChatCollectService)
            {
                if (string.IsNullOrWhiteSpace(PostText.Value))
                    return;
                model.ChatCollectServiceModule.PostChat(
                    SelectedPostService.Value,
                    new Model.ChatCollectService.NiconicoChatCollectService.ChatPostObject(PostText.Value, mail184)
                );
            }
            else if (SelectedPostService.Value is Model.ChatCollectService.NewNiconicoJikkyouChatCollectService)
            {
                if (string.IsNullOrWhiteSpace(PostText.Value))
                    return;
                model.ChatCollectServiceModule.PostChat(
                    SelectedPostService.Value,
                    new Model.ChatCollectService.NewNiconicoJikkyouChatCollectService.ChatPostObject(PostText.Value, mail184)
                );
            }
            else if (SelectedPostService.Value is Model.ChatCollectService.NiconicoLiveChatCollectService)
            {
                if (string.IsNullOrWhiteSpace(PostText.Value))
                    return;
                model.ChatCollectServiceModule.PostChat(
                    SelectedPostService.Value,
                    new Model.ChatCollectService.NiconicoLiveChatCollectService.ChatPostObject(PostText.Value, mail184)
                );
            }
            else if (SelectedPostService.Value is Model.ChatCollectService.NichanChatCollectService)
            {
                if (SelectedNichanCurrentThread.Value == null)
                    return;
                model.ChatCollectServiceModule.PostChat(
                    SelectedPostService.Value,
                    new Model.ChatCollectService.NichanChatCollectService.ChatPostObject(SelectedNichanCurrentThread.Value.Uri.ToString())
                );
            }
            else if (SelectedPostService.Value is Model.ChatCollectService.TwitterLiveChatCollectService)
            {
                if (string.IsNullOrWhiteSpace(PostText.Value))
                    return;
                model.ChatCollectServiceModule.PostChat(
                    SelectedPostService.Value,
                    new Model.ChatCollectService.TwitterLiveChatCollectService.ChatPostObject(PostText.Value, PostMailText.Value)
                );
            }
            else
                throw new Exception("Unknown ChatCollectService to post: " + SelectedPostService.Value.ToString());

            PostText.Value = "";
        }

        private void AddPostMailTextExample(string postMailTextExample)
        {
            if (string.IsNullOrWhiteSpace(postMailTextExample))
                return;
            PostMailTextExamples.Add(postMailTextExample);
        }

        private void RemovePostMailTextExample(string postMailTextExample)
        {
            PostMailTextExamples.Remove(postMailTextExample);
        }
    }
}
