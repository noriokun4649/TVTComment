using System;
using System.Reactive;
using System.Reactive.Disposables;

namespace TVTComment.ViewModels.Contents
{
    class ChatModRuleListItemViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject, IDisposable
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private Model.ChatModRules.IChatModRule chatModRule;
        private int appliedCount;
        private DateTime? lastAppliedTime;

        public Model.ChatModRules.IChatModRule ChatModRule { get { return chatModRule; } set { SetProperty(ref chatModRule, value); } }
        public int AppliedCount { get { return appliedCount; } set { SetProperty(ref appliedCount, value); } }
        public DateTime? LastAppliedTime { get { return lastAppliedTime; } set { SetProperty(ref lastAppliedTime, value); } }

        public ChatModRuleListItemViewModel(Model.ChatModRuleEntry source, IObservable<Unit> update)
        {
            ChatModRule = source.ChatModRule;
            AppliedCount = source.AppliedCount;
            LastAppliedTime = source.LastAppliedTime;

            disposables.Add(update.Subscribe(_ =>
            {
                AppliedCount = source.AppliedCount;
                LastAppliedTime = source.LastAppliedTime;
            }));
        }

        public void Dispose()
        {
            disposables.Dispose();
        }
    }
}
