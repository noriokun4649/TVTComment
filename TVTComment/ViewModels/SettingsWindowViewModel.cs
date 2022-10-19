using ObservableUtils;
using Prism.Commands;
using Prism.Interactivity.InteractionRequest;
using Prism.Mvvm;
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Windows.Input;
using System.Xml;
using TVTComment.Model.NiconicoUtils;
using TVTComment.Model.TwitterUtils;
using TVTComment.Model.TwitterUtils.AnnictUtils;

namespace TVTComment.ViewModels
{
    class SettingsWindowViewModel : BindableBase
    {
        public ObservableValue<int> ChatPreserveCount { get; }
        public ShellContents.DefaultChatCollectServicesViewModel DefaultChatCollectServices { get; }
        public ObservableValue<string> NiconicoLoginStatus { get; } = new ObservableValue<string>();
        public ObservableValue<string> NiconicoUserId { get; } = new ObservableValue<string>();
        public ObservableValue<string> NiconicoPassword { get; } = new ObservableValue<string>();
        public ObservableValue<string> NiconicoOneTimePassword { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanResCollectInterval { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanThreadSearchInterval { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiHmKey { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiAppKey { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiAuthUserAgent { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiAuthX2chUA { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiUserAgent { get; } = new ObservableValue<string>();
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

        public InteractionRequest<Notification> AlertRequest { get; } = new InteractionRequest<Notification>();

        public SettingsWindowContents.ChatCollectServiceCreationPresetSettingControlViewModel ChatCollectServiceCreationPresetSettingControlViewModel { get; }

        private readonly Model.ChatService.NiconicoChatService niconico;
        private readonly Model.ChatService.NichanChatService nichan;
        private readonly Model.ChatService.TwitterChatService twitter;

        private TwitterAuthentication twitterAuthentication;

        public SettingsWindowViewModel(Model.TVTComment model)
        {
            var secrets = App.Configuration;
            var annict = new AnnictAuthentication(secrets["Annict:ClientId"], secrets["Annict:ClientSecret"]);
            DefaultChatCollectServices = new ShellContents.DefaultChatCollectServicesViewModel(model);

            niconico = model.ChatServices.OfType<Model.ChatService.NiconicoChatService>().Single();
            nichan = model.ChatServices.OfType<Model.ChatService.NichanChatService>().Single();
            twitter = model.ChatServices.OfType<Model.ChatService.TwitterChatService>().Single();

            ChatCollectServiceCreationPresetSettingControlViewModel = new SettingsWindowContents.ChatCollectServiceCreationPresetSettingControlViewModel(model);

            AnnictAutoEnable.Value = twitter.AnnictAutoEnable;
            AnnictAutoEnable.Subscribe(par => twitter.AnnictAutoEnable = par);

            Always184.Value = niconico.Always184;
            Always184.Subscribe(state => niconico.Always184 = state);

            LoginNiconicoCommand = new DelegateCommand(async () =>
              {
                  if (string.IsNullOrWhiteSpace(NiconicoUserId.Value) || string.IsNullOrWhiteSpace(NiconicoPassword.Value))
                      return;

                  try
                  {
                      await niconico.SetUser(NiconicoUserId.Value, NiconicoPassword.Value, NiconicoOneTimePassword.Value);
                      SyncNiconicoUserStatus();
                  }
                  catch (Model.NiconicoUtils.NiconicoLoginSessionException)
                  {
                      AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = "ニコニコへのログインに失敗しました" });
                  }
              });

            ThirdForceApplyCommand = new DelegateCommand(async () =>
            {
                if (EnableThirdForce.Value)
                {
                    if (string.IsNullOrWhiteSpace(ThirdForceApiUri.Value))
                    {
                        AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = "URIが空腹です" });
                        return;
                    }
                    try
                    {
                        await NiconicoForceValidation.CheckAsync(ThirdForceApiUri.Value);

                        niconico.EnableThirdForce = EnableThirdForce.Value;
                        niconico.ThirdForceApiUri = ThirdForceApiUri.Value;
                        AlertRequest.Raise(new Notification { Title = "TVTCommentメッセージ", Content = "適用しました" });
                    }
                    catch (InvalidOperationException e)
                    {
                        AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = "無効なURIのため適用できません\n\n" + e.Message });
                        niconico.EnableThirdForce = false;
                    }
                    catch (HttpRequestException e)
                    {
                        AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = "HTTPリクエストエラーが発生したため適用できません\n\n" + e.Message });
                        niconico.EnableThirdForce = false;
                    }
                    catch (XmlException e)
                    {
                        AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = "XMLデータとして扱えない情報のため適用できません\n\n" + e.Message });
                        niconico.EnableThirdForce = false;
                    }
                    catch (ValidationException e)
                    {
                        AlertRequest.Raise(new Notification
                        {
                            Title = "TVTCommentエラー",
                            Content = "XMLデータのバリデーションチェックに失敗しました\n形式が違うため旧ニコニコ実況互換の勢いAPIとして利用出来ません\n\n" + e.Message
                        });
                        niconico.EnableThirdForce = false;
                    }
                }
                else
                {
                    niconico.EnableThirdForce = EnableThirdForce.Value;
                    AlertRequest.Raise(new Notification { Title = "TVTCommentメッセージ", Content = "適用しました" });
                }
                SyncThirdForceStatus();

            });

            ApplyNichanSettingsCommand = new DelegateCommand(() =>
              {
                  if (string.IsNullOrWhiteSpace(NichanResCollectInterval.Value) || string.IsNullOrWhiteSpace(NichanThreadSearchInterval.Value))
                      return;
                  try
                  {
                      nichan.SetIntervalValues(
                          TimeSpan.FromSeconds(uint.Parse(NichanResCollectInterval.Value)),
                          TimeSpan.FromSeconds(uint.Parse(NichanThreadSearchInterval.Value)));

                      nichan.SetApiParams(
                          NichanApiHmKey.Value, NichanApiAppKey.Value, nichan.GochanApiUserId, nichan.GochanApiPassword,
                          NichanApiAuthUserAgent.Value, NichanApiAuthX2chUA.Value, NichanApiUserAgent.Value
                      );

                      nichan.SetPastCollectServiceBackTime(TimeSpan.FromMinutes(double.Parse(NichanPastCollectServiceBackTime.Value)));

                      SyncNichanSettings();
                  }
                  catch (Exception e) when (e is FormatException || e is OverflowException)
                  {
                      AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = "2ch設定の値が不正です" });
                  }
              });

            ApplyTwitterApisCommand = new DelegateCommand(() =>
            {
                twitter.ApiKey = TwitterApiKey.Value;
                twitter.ApiSecret = TwitterApiSecret.Value;
                twitter.BearerToken = TwitterBearerToken.Value;
                AlertRequest.Raise(new Notification { Title = "TVTCommentメッセージ", Content = "適用しました" });
            });

            LoginTokensTwitterCommand = new DelegateCommand(async () =>
            {
                try
                {
                    await twitter.LoginAccessTokens(TwitterApiKey.Value, TwitterApiSecret.Value, TwitterApiAccessKey.Value, TwitterApiAccessSecret.Value, TwitterBearerToken.Value);
                    SyncTwitterStatus();
                }
                catch (Exception e)
                {
                    AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = e.Message });
                }
            });


            OpenTwitter = new DelegateCommand(() =>
            {
                try
                {
                    twitterAuthentication = twitter.InitOAuthPin(twitter.ApiKey, twitter.ApiSecret);
                    var oAuthSession = twitterAuthentication.AuthSession;
                    Process.Start(new ProcessStartInfo(oAuthSession.AuthorizeUri.ToString()) { UseShellExecute = true });
                }
                catch (Exception e)
                {
                    AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = e.Message });
                }
            });

            EnterTwitter = new DelegateCommand(async () =>
            {
                try
                {
                    await twitter.LoginOAuthPin(twitterAuthentication, TwitterPinCode.Value);
                    SyncTwitterStatus();
                }
                catch (Exception e)
                {
                    AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = e.Message });
                }

            });

            LogoutTwitterCommand = new DelegateCommand(() =>
            {
                try
                {
                    twitter.Logout();
                }
                catch (Exception e)
                {
                    AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = e.Message });
                }
                SyncTwitterStatus();
            });

            AnnictAccessTokenApplyCommand = new DelegateCommand(() => {
                try
                {
                    twitter.SetAnnictToken(AnnictAccessToken.Value);
                }
                catch (Exception e)
                {
                    AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = e.Message });
                }
                SyncAnnictStatus();
            });

            AnnictOAuthOpenCommand = new DelegateCommand(() => {
                try
                {
                    Process.Start(new ProcessStartInfo(annict.GetAuthorizeUri().ToString()) { UseShellExecute = true });
                }
                catch (Exception e)
                {
                    AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = e.Message });
                }
            });

            AnnictOAuthCertificationCommand = new DelegateCommand(async () => {
                try
                {
                    var token = await annict.GetTokenAsync(AnnictPin.Value);
                    twitter.SetAnnictToken(token);
                }
                catch (Exception e)
                {
                    AlertRequest.Raise(new Notification { Title = "TVTCommentエラー", Content = e.Message });
                }
                SyncAnnictStatus();
            });

            ChatPreserveCount = model.ChatModule.ChatPreserveCount;

            SyncNiconicoUserStatus();
            SyncNichanSettings();
            SyncTwitterStatus();
            SyncAnnictStatus();
            SyncThirdForceStatus();
        }

        private void SyncNiconicoUserStatus()
        {
            NiconicoLoginStatus.Value = niconico.IsLoggedin ? "ログイン済" : "未ログイン";
            NiconicoUserId.Value = niconico.UserId;
            NiconicoPassword.Value = niconico.UserPassword;
        }

        private void SyncNichanSettings()
        {
            NichanResCollectInterval.Value = nichan.ResCollectInterval.TotalSeconds.ToString();
            NichanThreadSearchInterval.Value = nichan.ThreadSearchInterval.TotalSeconds.ToString();
            NichanApiHmKey.Value = nichan.GochanApiHmKey;
            NichanApiAppKey.Value = nichan.GochanApiAppKey;
            NichanApiAuthUserAgent.Value = nichan.GochanApiAuthUserAgent;
            NichanApiAuthX2chUA.Value = nichan.GochanApiAuthX2UA;
            NichanApiUserAgent.Value = nichan.GochanApiUserAgent;
            NichanPastCollectServiceBackTime.Value = nichan.PastCollectServiceBackTime.TotalMinutes.ToString();
            NichanPastUserAgent.Value = nichan.GochanPastUserAgent;
        }

        private void SyncTwitterStatus()
        {
            TwitterApiKey.Value = twitter.ApiKey;
            TwitterApiSecret.Value = twitter.ApiSecret;
            TwitterApiAccessKey.Value = twitter.ApiAccessToken;
            TwitterApiAccessSecret.Value = twitter.ApiAccessSecret;
            TwitterBearerToken.Value = twitter.BearerToken;
            TwitterStatus.Value = twitter.IsLoggedin ? twitter.UserName + "としてログイン中" : "未ログイン";
        }

        private void SyncAnnictStatus()
        {
            AnnictAccessToken.Value = twitter.AnnictAccessToken;
        }

        private void SyncThirdForceStatus()
        {
            EnableThirdForce.Value = niconico.EnableThirdForce;
            ThirdForceApiUri.Value = niconico.ThirdForceApiUri;
        }
    }
}
