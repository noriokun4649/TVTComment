using ObservableUtils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace TVTComment.Model
{
    /// <summary>
    /// <seealso cref="IChatCollectService"/>周りの処理をする
    /// </summary>
    class ChatCollectServiceModule : IDisposable
    {
        private readonly ChannelInformationModule channelInformationManager;
        private readonly Timer timer;
        private readonly ObservableCollection<ChatCollectService.IChatCollectService> registeredServices = new ObservableCollection<ChatCollectService.IChatCollectService>();

        /// <summary>
        /// 取得する時刻の調整
        /// <see cref="SynchronizationContext"/>上から参照する必要がある
        /// </summary>
        public ObservableValue<TimeSpan> TimeAdjustment { get; } = new ObservableValue<TimeSpan>();
        /// <summary>
        /// 登録されている<seealso cref="IChatCollectService"/>のリスト
        /// <see cref="SynchronizationContext"/>上から参照する必要がある
        /// </summary>
        public ReadOnlyObservableCollection<ChatCollectService.IChatCollectService> RegisteredServices { get; }

        public delegate void NewChatProducedEventHandler(IEnumerable<Chat> newChats);
        /// <summary>
        /// 新たにチャットが生成されたときに発火する
        /// <see cref="SynchronizationContext"/>上で発火する
        /// </summary>
        public event NewChatProducedEventHandler NewChatProduced;

        public delegate void ErrorOccurredInChatCollectingEventHandler(ChatCollectService.IChatCollectService service, string errorText);
        /// <summary>
        /// ChatCollectServiceでエラーが発生したときに発火する
        /// <see cref="SynchronizationContext"/>上で発火する
        /// </summary>
        public event ErrorOccurredInChatCollectingEventHandler ErrorOccurredInChatCollecting;

        public delegate void ErrorOccurredInServiceCreationEventHandler(ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, string message);
        /// <summary>
        /// <see cref="AddService(IChatCollectServiceEntry, IChatCollectServiceCreationOption)"/>でサービス作成に失敗した時に呼ばれる
        /// </summary>
        public event ErrorOccurredInServiceCreationEventHandler ErrorOccurredInServiceCreation;

        public delegate void ErrorOccurredInChatPostingEventHandler(ChatCollectService.IChatCollectService service, ChatCollectService.BasicChatPostObject postObject, string errorText);
        /// <summary>
        /// <see cref="PostChat(IChatCollectService, BasicChatPostObject)"/>で投稿に失敗した時に呼ばれる
        /// </summary>
        public event ErrorOccurredInChatPostingEventHandler ErrorOccurredInChatPosting;

        /// <summary>
        /// コンストラクタの引数で指定した<seealso cref="ChannelInformationModule"/>の<seealso cref="ChannelInformationModule.SynchronizationContext"/>と同じ
        /// </summary>
        public SynchronizationContext SynchronizationContext => channelInformationManager.SynchronizationContext;

        public ChatCollectServiceModule(ChannelInformationModule channelInformationManager)
        {
            this.channelInformationManager = channelInformationManager;
            timer = new Timer(TimerCallback, null, 100, 50);
            RegisteredServices = new ReadOnlyObservableCollection<ChatCollectService.IChatCollectService>(registeredServices);
        }

        private void TimerCallback(object state)
        {
            var channel = channelInformationManager.CurrentChannel.Value;
            var events = channelInformationManager.CurrentEvent.Value;
            var time = channelInformationManager.CurrentTime.Value;
            if (channel != null && time.HasValue && events != null)
            {
                SynchronizationContext.Post((_) =>
                {
                    for (int i = RegisteredServices.Count - 1; i >= 0; i--)
                    {
                        ChatCollectService.IChatCollectService service = RegisteredServices[i];
                        try
                        {
                            NewChatProduced?.Invoke(service.GetChats(channel, events, time.Value + TimeAdjustment.Value).Select(chat =>
                              {
                                  chat.SetSourceService(service.ServiceEntry);
                                  return chat;
                              }));
                        }
                        catch (ChatCollectService.ChatCollectException e)
                        {
                            registeredServices.Remove(service);
                            ErrorOccurredInChatCollecting?.Invoke(service, e.Message);
                            service.Dispose();
                        }
                    }
                }, null);
            }
        }

        public void AddService(ChatCollectServiceEntry.IChatCollectServiceEntry serviceEntry, ChatCollectServiceEntry.IChatCollectServiceCreationOption creationOption)
        {
            try
            {
                registeredServices.Add(serviceEntry.GetNewService(creationOption));
            }
            catch (ChatCollectServiceEntry.ChatCollectServiceCreationException e)
            {
                ErrorOccurredInServiceCreation?.Invoke(serviceEntry, e.Message);
            }
        }

        public void RemoveService(ChatCollectService.IChatCollectService service)
        {
            registeredServices.Remove(service);
            service.Dispose();
        }

        public void ClearServices()
        {
            foreach (var service in registeredServices)
                service.Dispose();
            registeredServices.Clear();
        }

        public async void PostChat(ChatCollectService.IChatCollectService service, ChatCollectService.BasicChatPostObject postObject)
        {
            if (!service.CanPost)
            {
                ErrorOccurredInChatPosting(service, postObject, "このコメント元はコメント投稿をサポートしていません");
                return;
            }
            try
            {
                await service.PostChat(postObject);
            }
            catch (ChatCollectService.ChatPostException e)
            {
                ErrorOccurredInChatPosting(service, postObject, e.Message);
            }
        }

        public void Dispose()
        {
            timer.Dispose();
            foreach (var service in RegisteredServices)
                service.Dispose();
        }
    }
}
