using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly HashSet<IChatTrendService> updatingServices = new HashSet<IChatTrendService>();
        private bool disposed;
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
            if (disposed)
                return;
            IChatTrendService service = await serviceEntry.GetNewService();
            if (disposed)
            {
                service.Dispose();
                return;
            }
            if (timers.ContainsKey(service))
                return;
            timers.TryAdd(service, new Timer(TimerCallback, service, 1000, (int)service.UpdateInterval.TotalMilliseconds));
            registeredServices.Add(service);
        }

        public void RemoveService(IChatTrendService service)
        {
            if (!timers.TryRemove(service, out Timer timer))
                return;
            registeredServices.Remove(service);
            DisposeService(service, timer);
        }

        private void TimerCallback(object state)
        {
            ForceValueUpdatedSynchronizationContext.Post(async (chatTrendService) =>
            {
                IChatTrendService service = (IChatTrendService)chatTrendService;
                // 指定同期コンテキスト上での重複取得の防止
                if (disposed || !timers.TryGetValue(service, out Timer timer) || !updatingServices.Add(service))
                    return;
                IForceValueData forceValueData;

                try
                {
                    forceValueData = await service.GetForceValueData();
                }
                catch (ChatTrendServiceException)
                {
                    if (timers.TryGetValue(service, out Timer current) && current == timer)
                        RemoveService(service);
                    return;
                }
                finally
                {
                    updatingServices.Remove(service);
                }
                if (disposed || !timers.TryGetValue(service, out Timer active) || active != timer)
                    return;

                var item = forceValues.FirstOrDefault(x => x.Item1 == chatTrendService);
                if (item != null)
                    forceValues.Remove(item);
                forceValues.Add(new Tuple<IChatTrendService, IForceValueData>(service, forceValueData));

                ForceValueUpdated?.Invoke(this, new ForceValueUpdatedEventArgs(service, forceValueData));

            }, state);
        }

        private void DisposeService(IChatTrendService chatTrendService, Timer timer)
        {
            timer.Dispose();//Timer破棄
            chatTrendService.Dispose();//ChatTrendService破棄
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            foreach (var pair in timers)
                RemoveService(pair.Key);
        }
    }
}
