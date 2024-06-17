using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace TVTComment.Model
{
    class ChatTrendServiceModule : IDisposable
    {
        public class ForceValueUpdatedEventArgs
        {
            public IChatTrendService ChatTrendService { get; }
            public IForceValueData ForceValueData { get; }
            public ForceValueUpdatedEventArgs(IChatTrendService chatTrendService, IForceValueData forceValueData)
            {
                ChatTrendService = chatTrendService;
                ForceValueData = forceValueData;
            }
        }

        public SynchronizationContext ForceValueUpdatedSynchronizationContext { get; }
        private readonly ConcurrentDictionary<IChatTrendService, Timer> timers = new ConcurrentDictionary<IChatTrendService, Timer>();
        private readonly ObservableCollection<IChatTrendService> registeredServices = new ObservableCollection<IChatTrendService>();
        private readonly ObservableCollection<Tuple<IChatTrendService, IForceValueData>> forceValues = new ObservableCollection<Tuple<IChatTrendService, IForceValueData>>();

        /// <summary>
        /// 登録されている<seealso cref="IChatTrendService"/>のリスト
        /// コンストラクタの引数で指定した<seealso cref="SynchronizationContext"/>上で操作される
        /// </summary>
        public ReadOnlyObservableCollection<IChatTrendService> RegisteredServices { get; }

        public ReadOnlyObservableCollection<Tuple<IChatTrendService, IForceValueData>> ForceValues { get; }//TODO: Tuple使ってるしIForceValueDataそのまま公開してて手抜き

        public event EventHandler<ForceValueUpdatedEventArgs> ForceValueUpdated;

        /// <summary>
        /// 同期コンテキストを指定して<see cref="ChatTrendServiceModule"/>を初期化する
        /// </summary>
        /// <param name="forceValueUpdatedSynchronizationContext"><para><see cref="ForceValueUpdated"/>を呼び出す同期コンテキスト</para>
        /// <para><see cref="RegisteredServices"/>に登録された<seealso cref="IChatTrendService"/>の操作もこのコンテキスト上で行われる</para></param>
        public ChatTrendServiceModule(SynchronizationContext forceValueUpdatedSynchronizationContext)
        {
            ForceValueUpdatedSynchronizationContext = forceValueUpdatedSynchronizationContext;
            RegisteredServices = new ReadOnlyObservableCollection<IChatTrendService>(registeredServices);
            ForceValues = new ReadOnlyObservableCollection<Tuple<IChatTrendService, IForceValueData>>(forceValues);
        }

        public async void AddService(IChatTrendServiceEntry serviceEntry)
        {
            IChatTrendService service = await serviceEntry.GetNewService();
            timers.TryAdd(service, new Timer(TimerCallback, service, 1000, (int)service.UpdateInterval.TotalMilliseconds));
            registeredServices.Add(service);
        }

        public void RemoveService(IChatTrendService service)
        {
            registeredServices.Remove(service);
            timers.TryRemove(service, out Timer timer);
            DisposeService(service, timer);
        }

        private void TimerCallback(object state)
        {
            ForceValueUpdatedSynchronizationContext.Post(async (chatTrendService) =>
            {
                IChatTrendService service = (IChatTrendService)chatTrendService;
                IForceValueData forceValueData;

                try
                {
                    forceValueData = await service.GetForceValueData();
                }
                catch (ChatTrendServiceException)
                {
                    RemoveService(service);
                    //TODO: サービスが消えたことを伝える
                    return;
                }

                var item = forceValues.FirstOrDefault(x => x.Item1 == chatTrendService);
                if (item != null)
                    forceValues.Remove(item);
                forceValues.Add(new Tuple<IChatTrendService, IForceValueData>(service, forceValueData));

                ForceValueUpdated?.Invoke(this, new ForceValueUpdatedEventArgs(service, forceValueData));

            }, state);
        }

        private void DisposeService(IChatTrendService chatTrendService, Timer timer)
        {
            ManualResetEvent waitHandle = new ManualResetEvent(false);
            timer?.Dispose(waitHandle);//Timer破棄
            waitHandle.WaitOne();
            chatTrendService.Dispose();//ChatTrendService破棄
        }

        public void Dispose()
        {
            foreach (var pair in timers)
                DisposeService(pair.Key, pair.Value);
        }
    }
}
