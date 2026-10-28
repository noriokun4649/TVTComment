using Prism.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Windows.Input;

namespace TVTComment.ViewModels.ChatCollectServiceCreationOptionControl
{
    class NichanChatCollectServiceCreationOptionControlViewModel : ChatCollectServiceCreationOptionControlViewModel
    {
        private CancellationTokenSource refreshCancellation;
        private string refreshStatus;
        public string RefreshStatus
        {
            get { return refreshStatus; }
            private set { SetProperty(ref refreshStatus, value); }
        }

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
            refreshCancellation?.Cancel();
            using var cancellation = new CancellationTokenSource();
            refreshCancellation = cancellation;
            Threads = new List<Nichan.Thread>();
            SelectedThread = null;
            if (SelectedBoard == null)
            {
                refreshCancellation = null;
                RefreshStatus = "";
                return;
            }
            var board = SelectedBoard;
            RefreshStatus = "スレ一覧を取得中…";
            try
            {
                var result = await Nichan.BoardClient.Shared.GetThreadsAsync(board.Uri, cancellation.Token);
                if (!cancellation.IsCancellationRequested && SelectedBoard == board)
                {
                    Threads = result;
                    RefreshStatus = $"{result.Count}スレッド";
                }
            }
            catch (OperationCanceledException)
            {
                if (refreshCancellation == cancellation)
                    RefreshStatus = "取得をキャンセルしました";
            }
            catch (Exception e) when (e is HttpRequestException || e is Nichan.NichanException || e is FormatException || e is IOException)
            {
                if (refreshCancellation == cancellation)
                    RefreshStatus = "一覧を取得できませんでした: " + e.Message;
            }
            finally
            {
                if (refreshCancellation == cancellation)
                    refreshCancellation = null;
            }
        }
    }
}
