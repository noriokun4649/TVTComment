using Prism.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows.Input;

namespace TVTComment.ViewModels.ChatCollectServiceCreationOptionControl
{
    class NichanChatCollectServiceCreationOptionControlViewModel : ChatCollectServiceCreationOptionControlViewModel
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public override event EventHandler Finished;

        public ICommand OkCommand { get; }
        public ICommand RefreshThreadsCommand { get; }

        private Model.ChatService.NichanChatService.BoardInfo selectedBoard;
        public Model.ChatService.NichanChatService.BoardInfo SelectedBoard
        {
            get { return selectedBoard; }
            set { SetProperty(ref selectedBoard, value); RefreshThreads(); }
        }

        public Nichan.Thread SelectedThread { get; set; }

        public IEnumerable<Model.ChatService.NichanChatService.BoardInfo> Boards { get; }

        private List<Nichan.Thread> threads;
        public List<Nichan.Thread> Threads
        {
            get { return threads; }
            set { SetProperty(ref threads, value); }
        }

        public string Keywords { get; set; }

        private Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption.ThreadSelectMethod method;
        public Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption.ThreadSelectMethod Method
        {
            get { return method; }
            set { SetProperty(ref method, value); }
        }

        public NichanChatCollectServiceCreationOptionControlViewModel(Model.TVTComment model)
        {
            var nichan = model.ChatServices.OfType<Model.ChatService.NichanChatService>().Single();
            Boards = nichan.BoardList;
            Method = Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption.ThreadSelectMethod.Auto;
            OkCommand = new DelegateCommand(() => Finished?.Invoke(this, new EventArgs()));
            RefreshThreadsCommand = new DelegateCommand(RefreshThreads);
        }

        public override Model.ChatCollectServiceEntry.IChatCollectServiceCreationOption GetChatCollectServiceCreationOption()
        {
            switch (Method)
            {
                case Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption.ThreadSelectMethod.Fixed:
                    if (SelectedThread == null) return null;
                    return new Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption(Method, SelectedThread.Uri, null, null);
                case Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption.ThreadSelectMethod.Keyword:
                    if (SelectedBoard == null || string.IsNullOrWhiteSpace(Keywords)) return null;
                    return new Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption(Method, SelectedBoard.Uri, null, Keywords.Split());
                case Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption.ThreadSelectMethod.Fuzzy:
                    if (SelectedThread == null) return null;
                    return new Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption(Method, SelectedBoard.Uri, SelectedThread.Title, null);
                case Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption.ThreadSelectMethod.Auto:
                    return new Model.ChatCollectServiceEntry.NichanChatCollectServiceEntry.ChatCollectServiceCreationOption(Method, null, null, null);
                default:
                    throw new Exception("Invalid Method");
            }
        }

        private async void RefreshThreads()
        {
            if (SelectedBoard == null) return;
            string boardUri = SelectedBoard.Uri.ToString();
            var uri = new Uri(boardUri);
            string boardHost = $"{uri.Scheme}://{uri.Host}";
            string boardName = uri.Segments[1];
            if (boardName.EndsWith('/'))
                boardName = boardName[..^1];

            byte[] subjectBytes = await httpClient.GetByteArrayAsync($"{boardHost}/{boardName}/subject.txt");
            string subject = Encoding.GetEncoding(932).GetString(subjectBytes);

            using var textReader = new StringReader(subject);
            List<Nichan.Thread> threadsInBoard = await Nichan.SubjecttxtParser.ParseFromStream(textReader).ToListAsync();

            foreach (var thread in threadsInBoard)
                thread.Uri = new Uri($"{boardHost}/test/read.cgi/{boardName}/{thread.Name}/l50");

            Threads = threadsInBoard;
        }
    }
}
