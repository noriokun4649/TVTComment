using ObservableUtils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading;

namespace TVTComment.Model
{
    /// <summary>
    /// ユーザーが設定した既定のコメント元の自動選択に関する処理をする
    /// </summary>
    class DefaultChatCollectServiceModule
    {
        private readonly TVTCommentSettings settings;
        private readonly ChannelInformationModule channelInformationModule;
        private readonly ChatCollectServiceModule collectServiceModule;
        private readonly IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> serviceEntryList;
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private bool? lastIsRecord = null;
        private bool lastIsEnabled = false;

        public ObservableValue<bool> IsEnabled { get; } = new ObservableValue<bool>();
        /// <summary>
        /// リアルタイムで放送中の番組の視聴中にデフォルトで選択する<seealso cref="IChatCollectServiceEntry"/>
        /// <see cref="SynchronizationContext"/>上から操作される
        /// </summary>
        public ObservableCollection<ChatCollectServiceEntry.IChatCollectServiceEntry> LiveChatCollectService { get; } = new ObservableCollection<ChatCollectServiceEntry.IChatCollectServiceEntry>();
        /// <summary>
        /// 録画番組の視聴中にデフォルトで選択する<seealso cref="IChatCollectServiceEntry"/>
        /// <see cref="SynchronizationContext"/>上から操作される
        /// </summary>
        public ObservableCollection<ChatCollectServiceEntry.IChatCollectServiceEntry> RecordChatCollectService { get; } = new ObservableCollection<ChatCollectServiceEntry.IChatCollectServiceEntry>();

        /// <summary>
        /// コンストラクタの引数で指定した<seealso cref="ChannelInformationModule"/>の<seealso cref="ChannelInformationModule.SynchronizationContext"/>と
        /// <seealso cref="ChatCollectServiceModule"/>の<seealso cref="ChatCollectServiceModule.SynchronizationContext"/>と同じ
        /// </summary>
        public SynchronizationContext SynchronizationContext => channelInformationModule.SynchronizationContext;

        public DefaultChatCollectServiceModule(
            TVTCommentSettings settings, ChannelInformationModule channelInformationModule,
            ChatCollectServiceModule collectServiceModule, IEnumerable<ChatCollectServiceEntry.IChatCollectServiceEntry> serviceEntryList
        )
        {
            this.settings = settings;
            this.channelInformationModule = channelInformationModule;
            this.collectServiceModule = collectServiceModule;
            this.serviceEntryList = serviceEntryList;

            LoadSettings();
            disposables.Add(channelInformationModule.CurrentTime.Subscribe(TimeChanged));
            disposables.Add(LiveChatCollectService.ObserveCollectionChanged(newServiceEntry =>
            {
                if (IsEnabled.Value && !lastIsRecord.GetValueOrDefault(true))
                    if (collectServiceModule.RegisteredServices.All(x => x.ServiceEntry != newServiceEntry))
                        collectServiceModule.AddService(newServiceEntry, null);
            }, oldServiceEntry =>
            {
                if (IsEnabled.Value && !lastIsRecord.GetValueOrDefault(true))
                    foreach (var service in collectServiceModule.RegisteredServices.Where(x => x.ServiceEntry == oldServiceEntry).ToArray())
                        collectServiceModule.RemoveService(service);
            }, () =>
            {
                if (IsEnabled.Value && !lastIsRecord.GetValueOrDefault(true))
                    collectServiceModule.ClearServices();
            }));
            disposables.Add(RecordChatCollectService.ObserveCollectionChanged(newServiceEntry =>
            {
                if (IsEnabled.Value && lastIsRecord.GetValueOrDefault(false))
                    if (collectServiceModule.RegisteredServices.All(x => x.ServiceEntry != newServiceEntry))
                        collectServiceModule.AddService(newServiceEntry, null);
            }, oldServiceEntry =>
            {
                if (IsEnabled.Value && lastIsRecord.GetValueOrDefault(false))
                    foreach (var service in collectServiceModule.RegisteredServices.Where(x => x.ServiceEntry == oldServiceEntry).ToArray())
                        collectServiceModule.RemoveService(service);
            }, () =>
            {
                if (IsEnabled.Value && lastIsRecord.GetValueOrDefault(false))
                    collectServiceModule.ClearServices();
            }));

            disposables.Add(IsEnabled.Subscribe(_ => TimeChanged(channelInformationModule.CurrentTime.Value)));
        }

        private void TimeChanged(DateTime? time)
        {
            if (!time.HasValue)
                return;

            bool isEnabled = IsEnabled.Value;
            bool isRecord = (GetDateTimeJstNow() - time.Value).TotalMinutes > 3;
            if (lastIsEnabled == isEnabled && isRecord == lastIsRecord)
                return; // 有効・無効、リアルタイム・録画、いずれも変化がなければ何もしない

            lastIsEnabled = isEnabled;
            lastIsRecord = isRecord;

            if (!isEnabled)
                return; // 無効の場合は何もしない

            if (isRecord)
            {
                //録画
                collectServiceModule.ClearServices();
                foreach (var serviceEntry in RecordChatCollectService)
                    collectServiceModule.AddService(serviceEntry, null);
            }
            else
            {
                //リアルタイム
                collectServiceModule.ClearServices();
                foreach (var serviceEntry in LiveChatCollectService)
                    collectServiceModule.AddService(serviceEntry, null);
            }
        }

        public void Dispose()
        {
            SaveSettings();
            disposables.Dispose();
        }

        internal void SaveSettings()
        {
            settings.UseDefaultChatCollectService = IsEnabled.Value;

            settings.LiveDefaultChatCollectServices = LiveChatCollectService.Select(x => x.Id).ToArray();
            settings.RecordDefaultChatCollectServices = RecordChatCollectService.Select(x => x.Id).ToArray();
        }

        private void LoadSettings()
        {
            IsEnabled.Value = settings.UseDefaultChatCollectService;

            foreach (string id in settings.LiveDefaultChatCollectServices)
            {
                var serviceEntry = serviceEntryList.SingleOrDefault(x => x.Id == id);
                if (serviceEntry != null)
                    LiveChatCollectService.Add(serviceEntry);
            }
            foreach (string id in settings.RecordDefaultChatCollectServices)
            {
                var serviceEntry = serviceEntryList.SingleOrDefault(x => x.Id == id);
                if (serviceEntry != null)
                    RecordChatCollectService.Add(serviceEntry);
            }
        }

        private static DateTime GetDateTimeJstNow()
        {
            return DateTime.SpecifyKind(DateTime.UtcNow.AddHours(9), DateTimeKind.Unspecified);
        }
    }
}
