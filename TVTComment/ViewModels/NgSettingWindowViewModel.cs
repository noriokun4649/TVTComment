﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Prism.Commands;
using Prism.Mvvm;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using ObservableUtils;
using System.Reactive;

namespace TVTComment.ViewModels
{
    class NgSettingWindowViewModel:BindableBase,IDisposable
    {
        private Model.TVTComment model;
        private CompositeDisposable disposables=new CompositeDisposable();

        private double windowTop = double.NaN;
        private double windowLeft = double.NaN;
        private double windowWidth = double.NaN;
        private double windowHeight = double.NaN;
        public double WindowTop
        {
            get
            {
                return windowTop;
            }
            set
            {
                SetProperty(ref windowTop, value);
            }
        }
        public double WindowLeft
        {
            get
            {
                return windowLeft;
            }
            set
            {
                SetProperty(ref windowLeft, value);
            }
        }
        public double WindowWidth
        {
            get
            {
                return windowWidth;
            }
            set
            {
                SetProperty(ref windowWidth, value);
            }
        }
        public double WindowHeight
        {
            get
            {
                return windowHeight;
            }
            set
            {
                SetProperty(ref windowHeight, value);
            }
        }

        public List<Contents.SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>> TargetChatCollectServiceEntries { get; }
        public string NgText { get; set; }
        public Contents.ChatModRuleListItemViewModel SelectedRule { get; set; }
        public ReadOnlyObservableCollection<Contents.ChatModRuleListItemViewModel> Rules { get; private set; }
        public ObservableValue<int> SmallOnMultiLineRuleLineCount { get; } = new ObservableValue<int>(2);

        public ICommand AddWordNgCommand { get; private set; }
        public ICommand AddUserNgCommand { get; private set; }
        public ICommand AddIroKomeNgCommand { get; private set; }
        public ICommand AddJyougeKomeNgCommand { get; private set; }
        public ICommand AddJyougeIroKomeNgCommand { get; private set; }
        public ICommand AddRandomizeColorRuleCommand { get; private set; }
        public ICommand AddSmallOnMultiLineRuleCommand { get; private set; }
        public ICommand RemoveRuleCommand { get; private set; }

        public NgSettingWindowViewModel(Model.TVTComment model)
        {
            this.model = model;

            System.Drawing.Rectangle rect = (System.Drawing.Rectangle)model.Settings["NgSettingWindowPosition"];
            if (rect.Width != 0 || rect.Height != 0)
            {
                WindowLeft = rect.Left;
                WindowTop = rect.Top;
                WindowWidth = rect.Width;
                WindowHeight = rect.Height;
            }

            TargetChatCollectServiceEntries=new List<Contents.SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>>(
                model.ChatServices.SelectMany(service => service.ChatCollectServiceEntries)
                .Select(x=>new Contents.SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>(x,true)));
            var updateTimer = Observable.Interval(TimeSpan.FromSeconds(3));
            disposables.Add((IDisposable)(Rules = model.ChatModule.ChatModRules.MakeOneWayLinkedCollection(x=>new Contents.ChatModRuleListItemViewModel(x,updateTimer.Select(_=>new Unit())))));

            AddWordNgCommand = new DelegateCommand(() =>
              {
                  if (string.IsNullOrWhiteSpace(NgText)) return;
                  model.ChatModule.AddChatModRule(new Model.ChatModRules.WordNgChatModRule(TargetChatCollectServiceEntries.Where(x=>x.IsSelected).Select(x=>x.Value),NgText));
              });

            AddUserNgCommand = new DelegateCommand(() =>
              {
                  if (string.IsNullOrWhiteSpace(NgText)) return;
                  model.ChatModule.AddChatModRule(new Model.ChatModRules.UserNgChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value), NgText));
              });

            AddIroKomeNgCommand = new DelegateCommand(() =>
              {
                  model.ChatModule.AddChatModRule(new Model.ChatModRules.IroKomeNgChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
              });

            AddJyougeKomeNgCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.JyougeKomeNgChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddJyougeIroKomeNgCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.JyougeIroKomeNgChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddRandomizeColorRuleCommand = new DelegateCommand(() =>
              {
                  model.ChatModule.AddChatModRule(new Model.ChatModRules.RandomizeColorChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
              });

            AddSmallOnMultiLineRuleCommand = new DelegateCommand(() =>
              {
                  model.ChatModule.AddChatModRule(new Model.ChatModRules.SmallOnMultiLineChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value),SmallOnMultiLineRuleLineCount.Value));
              });

            RemoveRuleCommand = new DelegateCommand(() =>
              {
                  if (SelectedRule == null) return;
                  model.ChatModule.RemoveChatModRule(SelectedRule.ChatModRule);
              });
        }

        public void Dispose()
        {
            System.Drawing.Rectangle rect = new System.Drawing.Rectangle((int)WindowLeft, (int)WindowTop, (int)WindowWidth, (int)WindowHeight);
            model.Settings["NgSettingWindowPosition"]=rect;
            disposables.Dispose();
        }
    }
}
