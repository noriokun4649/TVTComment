using ObservableUtils;
using Prism.Commands;
using Prism.Interactivity.InteractionRequest;
using Prism.Mvvm;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using TVTComment.Model.TwitterUtils;
using TVTComment.Model.TwitterUtils.AnnictUtils;

namespace TVTComment.ViewModels
{
    class SettingsWindowViewModel : BindableBase
    {
        public ObservableValue<int> ChatPreserveCount { get; }
        public ShellContents.DefaultChatCollectServicesViewModel DefaultChatCollectServices { get; }
        public ObservableValue<string> NichanResCollectInterval { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanThreadSearchInterval { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiHmKey { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiAppKey { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiAuthUserAgent { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiAuthX2chUA { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanApiUserAgent { get; } = new ObservableValue<string>();
        public ObservableValue<string> NichanPastCollectServiceBackTime { get; } = new ObservableValue<string>();
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

            SyncNichanSettings();
            SyncTwitterStatus();
            SyncAnnictStatus();
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
    }
}
