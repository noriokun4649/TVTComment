using ObservableUtils;
using CommunityToolkit.Mvvm.Input;
using TVTComment.ViewModels.Notifications;
using TVTComment.ViewModels.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Windows.Input;
using System.Xml;
using TVTComment.Model.NiconicoUtils;

namespace TVTComment.ViewModels
{
    class SettingsWindowViewModel : ObservableObject, IDisposable
    {
        public ObservableValue<int> ChatPreserveCount { get; }
        public ShellContents.DefaultChatCollectServicesViewModel DefaultChatCollectServices { get; }
        public ObservableValue<string> NiconicoLoginStatus { get; } = new ObservableValue<string>();
        public ObservableValue<bool> NiconicoRememberSession { get; } = new ObservableValue<bool>();
        public ObservableValue<string> NichanResCollectInterval { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanThreadSearchInterval { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanPastCollectServiceBackTime { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanPastUserAgent { get; } = new ObservableValue<string>();
        public ObservableValue<string> TwitterApiKey { get; } = new ObservableValue<string>();
        public ObservableValue<string> TwitterApiSecret { get; } = new ObservableValue<string>();
        public ObservableValue<string> TwitterBearerToken { get; } = new ObservableValue<string>();
        public ObservableValue<string> TwitterApiAccessKey { get; } = new ObservableValue<string>();
        public ObservableValue<string> TwitterApiAccessSecret { get; } = new ObservableValue<string>();
        public ObservableValue<string> TwitterStatus { get; } = new ObservableValue<string>();
        public ObservableValue<string> TwitterPinCode { get; } = new ObservableValue<string>();
        public ObservableValue<string> AnnictAccessToken { get; } = new ObservableValue<string>();
        public ObservableValue<string> AnnictPin { get; } = new ObservableValue<string>();
        public ObservableValue<bool> AnnictAutoEnable { get; } = new ObservableValue<bool>();
        public ObservableValue<bool> Always184 { get; } = new ObservableValue<bool>();
        public ObservableValue<bool> EnableThirdForce { get; } = new ObservableValue<bool>();
        public ObservableValue<string> ThirdForceApiUri { get; } = new ObservableValue<string>();

        public Model.ChatService.NichanChatService.BoardInfo SelectedNichanBoard { get; set; }

        public ICommand LoginNiconicoCommand { get; }
        public ICommand LogoutNiconicoCommand { get; }
        public ICommand ApplyNichanSettingsCommand { get; }
        public ICommand ApplyTwitterApisCommand { get; }
        public ICommand LoginTokensTwitterCommand { get; }
        public ICommand LogoutTokensTwitterCommand { get; }
        public ICommand LogoutTwitterCommand { get; }
        public ICommand OpenTwitter { get; }
        public ICommand EnterTwitter { get; }
        public ICommand AnnictOAuthOpenCommand { get; }
        public ICommand AnnictOAuthCertificationCommand { get; }
        public ICommand AnnictAccessTokenApplyCommand { get; }
        public ICommand ThirdForceApplyCommand { get; }

        private readonly IDialogService dialogService;

        public SettingsWindowContents.ChatCollectServiceCreationPresetSettingControlViewModel ChatCollectServiceCreationPresetSettingControlViewModel { get; }

        private readonly Model.ChatService.NiconicoChatService niconico;
        private readonly Model.ChatService.NichanChatService nichan;
        private readonly IDisposable rememberSessionSubscription;

        public SettingsWindowViewModel(Model.TVTComment model, IDialogService dialogService)
        {
            this.dialogService = dialogService;
            var secrets = App.Configuration;
            DefaultChatCollectServices = new ShellContents.DefaultChatCollectServicesViewModel(model);

            niconico = model.ChatServices.OfType<Model.ChatService.NiconicoChatService>().Single();
            nichan = model.ChatServices.OfType<Model.ChatService.NichanChatService>().Single();

            ChatCollectServiceCreationPresetSettingControlViewModel = new SettingsWindowContents.ChatCollectServiceCreationPresetSettingControlViewModel(model, dialogService);

            Always184.Value = niconico.Always184;
            Always184.Subscribe(state => niconico.Always184 = state);

            LoginNiconicoCommand = new RelayCommand(async () =>
            {
                await niconico.LoginAsync();
            }, () => !niconico.IsBusy);
            LogoutNiconicoCommand = new RelayCommand(niconico.Logout);
            NiconicoRememberSession.Value = niconico.RememberSession;
            rememberSessionSubscription = NiconicoRememberSession.Subscribe(value => niconico.RememberSession = value);
            niconico.AuthenticationChanged += NiconicoAuthenticationChanged;

            ThirdForceApplyCommand = new RelayCommand(async () =>
            {
                if (EnableThirdForce.Value)
                {
                    if (string.IsNullOrWhiteSpace(ThirdForceApiUri.Value))
                    {
                        dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = "URIが空腹です" });
                        return;
                    }
                    try
                    {
                        await NiconicoForceValidation.CheckAsync(ThirdForceApiUri.Value);

                        niconico.EnableThirdForce = EnableThirdForce.Value;
                        niconico.ThirdForceApiUri = ThirdForceApiUri.Value;
                        dialogService.ShowNotification(new Notification { Title = "TVTCommentメッセージ", Content = "適用しました" });
                    }
                    catch (InvalidOperationException e)
                    {
                        dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = "無効なURIのため適用できません\n\n" + e.Message });
                    }
                    catch (HttpRequestException e)
                    {
                        dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = "HTTPリクエストエラーが発生したため適用できません\n\n" + e.Message });
                    }
                    catch (OperationCanceledException e)
                    {
                        dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = "勢いAPIの応答がタイムアウトしたため適用できません\n\n" + e.Message });
                    }
                    catch (XmlException e)
                    {
                        dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = "XMLデータとして扱えない情報のため適用できません\n\n" + e.Message });
                    }
                    catch (ValidationException e)
                    {
                        dialogService.ShowNotification(new Notification
                        {
                            Title = "TVTCommentエラー",
                            Content = "XMLデータのバリデーションチェックに失敗しました\n形式が違うため旧ニコニコ実況互換の勢いAPIとして利用出来ません\n\n" + e.Message
                        });
                    }
                }
                else
                {
                    niconico.EnableThirdForce = EnableThirdForce.Value;
                    dialogService.ShowNotification(new Notification { Title = "TVTCommentメッセージ", Content = "適用しました" });
                }
                SyncThirdForceStatus();

            });

            ApplyNichanSettingsCommand = new RelayCommand(() =>
              {
                  if (string.IsNullOrWhiteSpace(NichanResCollectInterval.Value) || string.IsNullOrWhiteSpace(NichanThreadSearchInterval.Value))
                      return;
                  try
                  {
                      nichan.SetIntervalValues(
                          TimeSpan.FromSeconds(uint.Parse(NichanResCollectInterval.Value)),
                          TimeSpan.FromSeconds(uint.Parse(NichanThreadSearchInterval.Value)));

                      nichan.SetPastCollectServiceBackTime(TimeSpan.FromMinutes(double.Parse(NichanPastCollectServiceBackTime.Value)));
                      nichan.SetPastUserAgent(NichanPastUserAgent.Value);

                      SyncNichanSettings();
                  }
                  catch (Exception e) when (e is FormatException || e is OverflowException)
                  {
                      dialogService.ShowNotification(new Notification { Title = "TVTCommentエラー", Content = "2ch設定の値が不正です" });
                  }
              });

            ChatPreserveCount = model.ChatModule.ChatPreserveCount;

            SyncNiconicoUserStatus();
            SyncNichanSettings();
            SyncThirdForceStatus();
        }

        private void SyncNiconicoUserStatus()
        {
            NiconicoLoginStatus.Value = niconico.AuthenticationMessage;
            ((RelayCommand)LoginNiconicoCommand).NotifyCanExecuteChanged();
        }

        private void NiconicoAuthenticationChanged(object sender, EventArgs e) => SyncNiconicoUserStatus();

        public void Dispose()
        {
            niconico.AuthenticationChanged -= NiconicoAuthenticationChanged;
            rememberSessionSubscription?.Dispose();
            niconico.CancelAuthentication();
        }

        private void SyncNichanSettings()
        {
            NichanResCollectInterval.Value = nichan.ResCollectInterval.TotalSeconds.ToString();
            NichanThreadSearchInterval.Value = nichan.ThreadSearchInterval.TotalSeconds.ToString();
            NichanPastCollectServiceBackTime.Value = nichan.PastCollectServiceBackTime.TotalMinutes.ToString();
            NichanPastUserAgent.Value = nichan.GochanPastUserAgent;
        }

        private void SyncThirdForceStatus()
        {
            EnableThirdForce.Value = niconico.EnableThirdForce;
            ThirdForceApiUri.Value = niconico.ThirdForceApiUri;
        }
    }
}
