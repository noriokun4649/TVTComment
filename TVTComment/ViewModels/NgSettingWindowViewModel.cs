using ObservableUtils;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;

namespace TVTComment.ViewModels
{
    class NgSettingWindowViewModel : BindableBase, IDisposable
    {
        private readonly Model.TVTComment model;
        private readonly CompositeDisposable disposables = new CompositeDisposable();

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
        public ObservableValue<int> TextLengthCount { get; } = new ObservableValue<int>(20);
        public ObservableValue<int> SmallOnMultiLineRuleLineCount { get; } = new ObservableValue<int>(2);
        public ObservableValue<Color> SetColorRuleColor { get; } = new ObservableValue<Color>(Color.FromArgb(255, 255, 255));
        public ObservableValue<string> ReplaceRegexPattern { get; } = new ObservableValue<string>("");
        public ObservableValue<string> ReplaceRegexReplacement { get; } = new ObservableValue<string>("");

        public ICommand AddWordNgCommand { get; private set; }
        public ICommand AddUserNgCommand { get; private set; }
        public ICommand AddIroKomeNgCommand { get; private set; }
        public ICommand AddJyougeKomeNgCommand { get; private set; }
        public ICommand AddJyougeIroKomeNgCommand { get; private set; }
        public ICommand AddTextLengthNgCommand { get; private set; }
        public ICommand AddRandomizeColorRuleCommand { get; private set; }
        public ICommand AddSmallOnMultiLineRuleCommand { get; private set; }
        public ICommand AddRemoveAnchorCommand { get; private set; }
        public ICommand AddRemoveUrlCommand { get; private set; }
        public ICommand AddRemoveHashtagCommand { get; private set; }
        public ICommand AddRemoveMentionCommand { get; private set; }
        public ICommand AddRenderEmotionAsCommentCommand { get; private set; }
        public ICommand AddRenderInfoAsCommentCommand { get; private set; }
        public ICommand AddSetColorRuleCommand { get; private set; }
        public ICommand AddRenderNicoadAsCommentCommand { get; private set; }
        public ICommand AddReplaceRegexCommand { get; private set; }
        public ICommand AddRegexNgCommand { get; private set; }
        public ICommand RemoveRuleCommand { get; private set; }
        public ICommand AddUrlNgCommand { get; private set; }
        public ICommand AddMentionNgCommand { get; private set; }

        public NgSettingWindowViewModel(Model.TVTComment model)
        {
            this.model = model;

            Model.Serialization.WindowPositionEntity rect = model.Settings.View.NgSettingWindowPosition;
            WindowLeft = rect.X;
            WindowTop = rect.Y;
            WindowWidth = rect.Width;
            WindowHeight = rect.Height;

            TargetChatCollectServiceEntries = new List<Contents.SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>>(
                model.ChatServices.SelectMany(service => service.ChatCollectServiceEntries)
                .Select(x => new Contents.SelectableViewModel<Model.ChatCollectServiceEntry.IChatCollectServiceEntry>(x, true)));
            var updateTimer = Observable.Interval(TimeSpan.FromSeconds(3));
            disposables.Add((IDisposable)(Rules = model.ChatModule.ChatModRules.MakeOneWayLinkedCollection(x => new Contents.ChatModRuleListItemViewModel(x, updateTimer.Select(_ => new Unit())))));

            AddWordNgCommand = new DelegateCommand(() =>
              {
                  if (string.IsNullOrWhiteSpace(NgText)) return;
                  model.ChatModule.AddChatModRule(new Model.ChatModRules.WordNgChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value), NgText));
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

            AddTextLengthNgCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.TextLengthNg(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value), TextLengthCount.Value));
            });

            AddRandomizeColorRuleCommand = new DelegateCommand(() =>
              {
                  model.ChatModule.AddChatModRule(new Model.ChatModRules.RandomizeColorChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
              });

            AddSmallOnMultiLineRuleCommand = new DelegateCommand(() =>
              {
                  model.ChatModule.AddChatModRule(new Model.ChatModRules.SmallOnMultiLineChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value), SmallOnMultiLineRuleLineCount.Value));
              });

            AddRemoveAnchorCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.RemoveAnchorChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddRemoveUrlCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.RemoveUrlChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddRemoveHashtagCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.RemoveHashtagChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddRemoveMentionCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.RemoveMentionChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddRenderEmotionAsCommentCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.RenderEmotionAsCommentChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddRenderInfoAsCommentCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.RenderInfoAsCommentChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddSetColorRuleCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.SetColorChatModRule(
                    TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value),
                    SetColorRuleColor.Value
                ));
            });

            AddRenderNicoadAsCommentCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.RenderNicoadAsCommentChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddReplaceRegexCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.ReplaceRegexChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value), ReplaceRegexPattern.Value, ReplaceRegexReplacement.Value));
            });

            AddRegexNgCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.RegexNgChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value), NgText));
            });

            AddUrlNgCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.UrlNgChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            AddMentionNgCommand = new DelegateCommand(() =>
            {
                model.ChatModule.AddChatModRule(new Model.ChatModRules.MentionNgChatModRule(TargetChatCollectServiceEntries.Where(x => x.IsSelected).Select(x => x.Value)));
            });

            RemoveRuleCommand = new DelegateCommand(() =>
            {
                if (SelectedRule == null) return;
                model.ChatModule.RemoveChatModRule(SelectedRule.ChatModRule);
            });
        }

        public void Dispose()
        {
            model.Settings.View.NgSettingWindowPosition = new Model.Serialization.WindowPositionEntity()
            {
                X = WindowLeft,
                Y = WindowTop,
                Width = WindowWidth,
                Height = WindowHeight,
            };

            disposables.Dispose();
        }
    }
}
