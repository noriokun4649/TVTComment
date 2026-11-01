using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using TVTComment.Model;
using TVTComment.Model.IPC.IPCMessage;

namespace TVTComment.ViewModels
{
    sealed class TVTestPanelBridge : IDisposable
    {
        private sealed class ItemId
        {
            public string Value;
        }

        private readonly IPCModule ipc;
        private readonly ShellViewModel viewModel;
        private readonly DispatcherTimer timer;
        private readonly ConditionalWeakTable<object, ItemId> ids = new ConditionalWeakTable<object, ItemId>();
        private long nextId, postRevision;
        private int subscriptionVersion;
        private bool subscribed, sending, disposed;
        private string[] lastChannels, lastSources, lastPosting;
        private Chat[] lastChats;

        public TVTestPanelBridge(IPCModule ipc, ShellViewModel viewModel)
        {
            this.ipc = ipc;
            this.viewModel = viewModel;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += TimerTick;
            ipc.MessageReceived += MessageReceived;
        }

        public async void Start()
        {
            // 初期化前に届いた購読要求を再送してもらう
            await Send(new PanelIPCMessage { Operation = "Ready", Arguments = new[] { Number(Environment.ProcessId) } });
        }

        private string Id(object item) => ids.GetValue(item, _ => new ItemId
        {
            Value = (++nextId).ToString(CultureInfo.InvariantCulture)
        }).Value;

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private async void TimerTick(object sender, EventArgs e) => await Publish();

        private async void MessageReceived(IIPCMessage message)
        {
            if (disposed || !(message is PanelIPCMessage panel)) return;
            var args = panel.Arguments;
            switch (panel.Operation)
            {
                case "Subscribe" when args.Length == 0:
                    ++subscriptionVersion;
                    subscribed = true;
                    lastChannels = lastSources = lastPosting = null;
                    lastChats = null;
                    timer.Start();
                    break;
                case "Unsubscribe" when args.Length == 0:
                    ++subscriptionVersion;
                    subscribed = false;
                    timer.Stop();
                    lastChats = null;
                    return;
                case "SelectChannel" when args.Length == 1:
                    Execute(viewModel.ChangeChannelCommand, viewModel.Channels.FirstOrDefault(x => Id(x) == args[0])?.Channel);
                    break;
                case "AddSource" when args.Length == 1:
                    await viewModel.AddChatCollectService(viewModel.ChatCollectServiceAddList.FirstOrDefault(x => Id(x) == args[0]), useDefaultCreationOption: true);
                    break;
                case "RemoveSource" when args.Length == 1:
                    Execute(viewModel.RemoveChatCollectServiceCommand, viewModel.ChatCollectServices.FirstOrDefault(x => Id(x) == args[0])?.Service);
                    break;
                case "AutoSources" when args.Length == 1 && (args[0] == "0" || args[0] == "1"):
                    viewModel.UseDefaultChatCollectService.Value = args[0] == "1";
                    break;
                case "ClearChats" when args.Length == 0:
                    Execute(viewModel.ClearChatsCommand);
                    break;
                case "ShowWindow" when args.Length == 0:
                    Execute(viewModel.WindowPresentation.ShowCommand);
                    break;
                case "Visibility" when args.Length == 1 && (args[0] == "0" || args[0] == "1"):
                    viewModel.WindowPresentation.SetPanelVisible(args[0] == "1");
                    return;
                case "PostDraft" when args.Length == 5:
                case "PostChat" when args.Length == 5:
                    if (ApplyPostDraft(args) && panel.Operation == "PostChat")
                        Execute(viewModel.ChatPostControlViewModel.PostCommand);
                    break;
                case "RefreshPostThreads" when args.Length == 0:
                    Execute(viewModel.ChatPostControlViewModel?.UpdateNichanCurrentThreadsCommand);
                    break;
                default:
                    return;
            }
            await Publish();
        }

        private static void Execute(ICommand command, object parameter = null)
        {
            if (command?.CanExecute(parameter) == true) command.Execute(parameter);
        }

        private bool ApplyPostDraft(string[] args)
        {
            var post = viewModel.ChatPostControlViewModel;
            if (post?.PostServices == null || !long.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out long revision)
                || revision <= postRevision) return false;
            postRevision = revision;
            post.PostText.Value = args[3];
            post.PostMailText.Value = args[4];
            var service = post.PostServices.FirstOrDefault(x => Id(x) == args[1]);
            if (service == null) return false;
            if (post.SelectedPostService.Value != service)
            {
                post.SelectedPostService.Value = service;
                Execute(post.UpdateNichanCurrentThreadsCommand);
            }
            if (ShellContents.ChatPostControlViewModel.PostForm(service) == "thread")
            {
                post.SelectedNichanCurrentThread.Value = post.NichanCurrentThreads.FirstOrDefault(x => Id(x) == args[2]);
                return post.SelectedNichanCurrentThread.Value != null;
            }
            return ShellContents.ChatPostControlViewModel.PostForm(service) == "text";
        }

        private string[] CreatePostingState()
        {
            var post = viewModel.ChatPostControlViewModel;
            if (post?.PostServices == null) return null;
            var selected = post.PostServices.Contains(post.SelectedPostService.Value) ? post.SelectedPostService.Value : null;
            var fields = new List<string>
            {
                postRevision.ToString(CultureInfo.InvariantCulture), selected == null ? "" : Id(selected),
                post.PostText.Value ?? "", post.PostMailText.Value ?? "", Number(post.PostServices.Count)
            };
            foreach (var service in post.PostServices)
                fields.AddRange(new[] { Id(service), service.Name ?? "", ShellContents.ChatPostControlViewModel.PostForm(service) });
            var thread = post.SelectedNichanCurrentThread.Value;
            fields.Add(thread != null && post.NichanCurrentThreads.Contains(thread) ? Id(thread) : "");
            fields.Add(Number(post.NichanCurrentThreads.Count));
            foreach (var item in post.NichanCurrentThreads)
                fields.AddRange(new[] { Id(item), item.Title ?? "" });
            return fields.ToArray();
        }

        private async Task Send(PanelIPCMessage message)
        {
            if (disposed || (ipc.State != IPCModuleState.Connected && ipc.State != IPCModuleState.Receiving)) return;
            try
            {
                await ipc.Send(message);
            }
            catch (InvalidOperationException) when (disposed || ipc.State == IPCModuleState.Disposing || ipc.State == IPCModuleState.Disposed)
            {
                // 終了処理中の送信エラーは無視
            }
        }

        private async Task Publish()
        {
            if (!subscribed || disposed || sending) return;
            sending = true;
            int version = subscriptionVersion;
            try
            {
                var messages = new List<PanelIPCMessage>();
                var channels = viewModel.Channels.SelectMany(x => new[]
                {
                    Id(x), x.Watching ? "1" : "0", x.Channel.ChannelName ?? "", x.ForceValue?.ToString(CultureInfo.InvariantCulture) ?? ""
                }).ToArray();
                if (lastChannels == null || !channels.SequenceEqual(lastChannels))
                {
                    messages.Add(new PanelIPCMessage { Operation = "Channels", Arguments = channels });
                    lastChannels = channels;
                }

                var sources = new List<string>
                {
                    viewModel.UseDefaultChatCollectService.Value ? "1" : "0",
                    viewModel.AddChatCollectServiceCommand.CanExecute(null) ? "1" : "0",
                    Number(viewModel.ChatCollectServiceAddList.Count)
                };
                foreach (var item in viewModel.ChatCollectServiceAddList)
                    sources.AddRange(new[] { Id(item), item.Title ?? "", item.Subtitle ?? "" });
                sources.Add(Number(viewModel.ChatCollectServices.Count));
                foreach (var item in viewModel.ChatCollectServices)
                    sources.AddRange(new[] { Id(item), item.Service.Name ?? "", item.InformationText ?? "" });
                if (lastSources == null || !sources.SequenceEqual(lastSources))
                {
                    lastSources = sources.ToArray();
                    messages.Add(new PanelIPCMessage { Operation = "Sources", Arguments = lastSources });
                }

                var posting = CreatePostingState();
                if (posting != null && (lastPosting == null || !posting.SequenceEqual(lastPosting)))
                {
                    lastPosting = posting;
                    messages.Add(new PanelIPCMessage { Operation = "Posting", Arguments = posting });
                }

                var chats = viewModel.Chats.ToArray();
                var chatUpdate = CreateChatUpdate(lastChats, chats, Id);
                if (chatUpdate != null) messages.Add(chatUpdate);
                lastChats = chats;

                foreach (var message in messages)
                {
                    foreach (var part in SplitChatUpdate(message))
                    {
                        if (!subscribed || disposed || version != subscriptionVersion) return;
                        await Send(part);
                    }
                }
            }
            finally
            {
                sending = false;
            }
        }

        // コメント描画の通信を妨げないように分割して送る
        internal static IEnumerable<PanelIPCMessage> SplitChatUpdate(PanelIPCMessage message)
        {
            const int batchFields = 128 * 6;
            if (message.Operation != "Chats" || message.Arguments.Length <= batchFields + 2)
            {
                yield return message;
                yield break;
            }
            for (int offset = 2; offset < message.Arguments.Length; offset += batchFields)
                yield return new PanelIPCMessage
                {
                    Operation = "Chats",
                    Arguments = (offset == 2 ? message.Arguments.Take(2) : new[] { "0", "0" })
                        .Concat(message.Arguments.Skip(offset).Take(batchFields)).ToArray()
                };
        }

        // 先頭の削除と末尾の追加は差分で送る
        internal static PanelIPCMessage CreateChatUpdate(Chat[] previous, Chat[] current, Func<object, string> id)
        {
            bool reset = previous == null;
            int removed = 0, kept = 0;
            if (!reset && previous.Length > 0 && current.Length > 0)
            {
                removed = Array.IndexOf(previous, current[0]);
                reset = removed < 0;
                if (!reset)
                {
                    kept = Math.Min(previous.Length - removed, current.Length);
                    reset = kept != previous.Length - removed || !previous.Skip(removed).Take(kept).SequenceEqual(current.Take(kept));
                }
            }
            else if (!reset)
                removed = previous.Length;

            if (reset) removed = kept = 0;
            if (!reset && removed == 0 && kept == current.Length) return null;
            var fields = new List<string> { reset ? "1" : "0", Number(removed) };
            foreach (var chat in current.Skip(kept))
                fields.AddRange(new[]
                {
                    id(chat), chat.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture), chat.Ng ? "###### NG ######" : chat.Text,
                    chat.IsSystemMsg ? "システム" : chat.SourceService?.Name ?? "", chat.UserId, Number(chat.Number)
                });
            return new PanelIPCMessage { Operation = "Chats", Arguments = fields.ToArray() };
        }

        public void Dispose()
        {
            disposed = true;
            subscribed = false;
            timer.Stop();
            timer.Tick -= TimerTick;
            ipc.MessageReceived -= MessageReceived;
            lastChats = null;
        }
    }
}
