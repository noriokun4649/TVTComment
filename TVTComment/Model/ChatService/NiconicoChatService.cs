using ObservableUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TVTComment.Model.NiconicoUtils;

namespace TVTComment.Model.ChatService
{
    class NiconicoChatServiceSettings
    {
        public string UserId { get; set; } = "";
        public bool RememberSession { get; set; } = true;
        public bool Always184 { get; set; } = true;
        public bool EnableThirdForce { get; set; } = false;
        public string ThirdForceApiUri { get; set; } = "";
    }

    class NiconicoChatService : IChatService
    {
        public string Name => "ニコニコ";
        public IReadOnlyList<ChatCollectServiceEntry.IChatCollectServiceEntry> ChatCollectServiceEntries { get; }
        public IReadOnlyList<IChatTrendServiceEntry> ChatTrendServiceEntries { get; }

        //このChatServiceに行われた設定変更が子のChatServiceEntryに伝わるようにするためにObservableValueで包む
        //private ObservableValue<Dictionary<uint, int>> jkIdTable = new ObservableValue<Dictionary<uint, int>>();
        private readonly ObservableValue<NiconicoUtils.NiconicoLoginSession> loginSession = new ObservableValue<NiconicoUtils.NiconicoLoginSession>();

        private readonly NiconicoUtils.LiveIdResolver liveIdResolver;
        private readonly NiconicoUtils.JkIdResolver jkIdResolver;
        private readonly NiconicoChatServiceSettings settings;
        private readonly NiconicoSessionStore store;
        private readonly NiconicoOAuthClient oauthClient;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource operation;
        private bool disposed;
        public event EventHandler AuthenticationChanged;
        public NiconicoAuthenticationState AuthenticationState { get; private set; }
        public string AuthenticationMessage { get; private set; } = "未ログイン";
        public bool IsBusy { get; private set; }
        public bool IsLoggedin => loginSession.Value != null;
        public string UserId
        {
            get { return settings.UserId; }
        }
        public bool Always184
        {
            get { return settings.Always184; }
            set { settings.Always184 = value; }
        }
        public bool EnableThirdForce
        {
            get { return settings.EnableThirdForce; }
            set { settings.EnableThirdForce = value; }
        }
        public string ThirdForceApiUri
        {
            get { return settings.ThirdForceApiUri; }
            set { settings.ThirdForceApiUri = value; }
        }
        public bool RememberSession
        {
            get => settings.RememberSession;
            set
            {
                settings.RememberSession = value;
                try { if (!value) store.Delete(); else if (loginSession.Value is NiconicoOAuthSession session) store.Save(session); }
                catch (Exception e) when (IsStorageError(e)) { SetState(AuthenticationState, "ログイン状態の保存設定を反映できませんでした。保存先へのアクセスを確認してください。"); }
            }
        }

        public NiconicoChatService(
            NiconicoChatServiceSettings settings, ChannelDatabase channelDatabase,
            string jikkyouIdTableFilePath, string liveIdTableFilePath,
            NiconicoOAuthClient oauthClient = null
        )
        {
            this.settings = settings;
            this.oauthClient = oauthClient ?? new NiconicoOAuthClient();
            store = new NiconicoSessionStore(Path.GetDirectoryName(liveIdTableFilePath));
            jkIdResolver = new NiconicoUtils.JkIdResolver(channelDatabase, new NiconicoUtils.JkIdTable(jikkyouIdTableFilePath));
            liveIdResolver = new NiconicoUtils.LiveIdResolver(channelDatabase, new NiconicoUtils.LiveIdTable(liveIdTableFilePath));

            ChatCollectServiceEntries = new ChatCollectServiceEntry.IChatCollectServiceEntry[] {
                new ChatCollectServiceEntry.NewNiconicoJikkyouChatCollectServiceEntry(this, liveIdResolver, loginSession),
                new ChatCollectServiceEntry.NiconicoLiveChatCollectServiceEntry(this, loginSession),
                new ChatCollectServiceEntry.TsukumijimaJikkyoApiChatCollectServiceEntry(this, jkIdResolver, loginSession),
                new ChatCollectServiceEntry.NxJikkyoChatCollectServiceEntry(this, jkIdResolver)
            };
            ChatTrendServiceEntries = new IChatTrendServiceEntry[] { new NiconicoChatTrendServiceEntry(jkIdResolver, settings) };
        }

        private void SetState(NiconicoAuthenticationState state, string message)
        {
            AuthenticationState = state;
            AuthenticationMessage = message;
            AuthenticationChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task RestoreAsync()
        {
            await RunOperationAsync(async token =>
            {
                if (!RememberSession) { store.Delete(); return; }
                var session = store.Load();
                if (session == null) return;
                SetState(NiconicoAuthenticationState.Restoring, "保存したログイン状態を確認中…");
                try
                {
                    await session.GetAccessTokenAsync(token);
                    token.ThrowIfCancellationRequested();
                    ApplySession(session);
                }
                catch (NiconicoSessionExpiredException)
                {
                    store.Delete();
                    SetState(NiconicoAuthenticationState.Expired, "ログインの有効期限が切れました。設定の「ログイン」から再度ログインしてください。");
                }
            });
            if (!disposed) _ = MonitorSessionAsync();
        }

        /// <summary>
        /// 取得したセッションの検証・適用と、失敗・キャンセル時の既存セッションの保持
        /// </summary>
        public Task LoginAsync() => RunOperationAsync(async token =>
        {
            SetState(NiconicoAuthenticationState.Authenticating, "ブラウザーでニコニコの認証を完了してください…");
            var credentials = await oauthClient.AuthorizeAsync(token);
            var session = new NiconicoOAuthSession(credentials, oauthClient);
            token.ThrowIfCancellationRequested();
            ApplySession(session);
        });

        public Task ReloginIfExpiredAsync()
        {
            return AuthenticationState == NiconicoAuthenticationState.Expired ? LoginAsync() : Task.CompletedTask;
        }

        private void ApplySession(NiconicoOAuthSession session)
        {
            (loginSession.Value as NiconicoOAuthSession)?.Invalidate();
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
            session.ChangedAsync = () => Task.Factory.StartNew(() => SaveSession(session),
                CancellationToken.None, TaskCreationOptions.None, scheduler);
            settings.UserId = session.UserId;
            loginSession.Value = session;
            SetState(NiconicoAuthenticationState.SignedIn, $"ログイン済（ユーザーID: {session.UserId}）");
            SaveSession(session);
        }

        private bool SaveSession(NiconicoOAuthSession session)
        {
            if (disposed || loginSession.Value != session) return true;
            if (session.IsExpired)
            {
                InvalidateSession(session);
                return true;
            }
            try { if (RememberSession) store.Save(session); else store.Delete(); }
            catch (Exception e) when (IsStorageError(e))
            {
                SetState(NiconicoAuthenticationState.SignedIn, "ログイン済。ログイン状態を保存できませんでした。次回起動時に設定の「ログイン」から再度ログインしてください。");
                return false;
            }
            return true;
        }

        private async Task RunOperationAsync(Func<CancellationToken, Task> action)
        {
            if (disposed || IsBusy) return;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            operation = cancellation;
            IsBusy = true;
            SetState(NiconicoAuthenticationState.Authenticating, "認証情報を確認中…");
            try
            {
                await action(cancellation.Token);
                if (AuthenticationState == NiconicoAuthenticationState.Authenticating) SetState(IsLoggedin ? NiconicoAuthenticationState.SignedIn : NiconicoAuthenticationState.SignedOut, IsLoggedin ? "ログイン済" : "未ログイン");
            }
            catch (OperationCanceledException)
            {
                SetState(IsLoggedin ? NiconicoAuthenticationState.SignedIn : NiconicoAuthenticationState.SignedOut,
                    "ログインをキャンセルしたか、通信がタイムアウトしました。設定の「ログイン」から再度ログインしてください。");
            }
            catch (Exception e) when (e is NiconicoLoginSessionException || e is HttpRequestException || e is FormatException || e is JsonException || IsStorageError(e))
            {
                var message = e is NiconicoLoginSessionException || e is FormatException ? e.Message : "ログイン状態を確認できませんでした。通信状態または保存データを確認し、設定の「ログイン」から再度ログインしてください。";
                SetState(IsLoggedin ? NiconicoAuthenticationState.SignedIn : NiconicoAuthenticationState.SignedOut, message);
            }
            finally
            {
                operation = null;
                IsBusy = false;
                AuthenticationChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void CancelAuthentication()
        {
            operation?.Cancel();
        }

        public void Logout()
        {
            CancelAuthentication();
            (loginSession.Value as NiconicoOAuthSession)?.Invalidate();
            loginSession.Value = null;
            try { store.Delete(); SetState(NiconicoAuthenticationState.SignedOut, "ログアウトしました"); }
            catch (Exception e) when (IsStorageError(e)) { SetState(NiconicoAuthenticationState.SignedOut, "ログイン状態を解除しましたが、保存したログイン情報を削除できませんでした。"); }
        }

        public void InvalidateSession(NiconicoLoginSession session)
        {
            if (disposed || session == null || loginSession.Value != session) return;
            (session as NiconicoOAuthSession)?.Invalidate();
            loginSession.Value = null;
            try { store.Delete(); } catch (Exception e) when (IsStorageError(e)) { }
            SetState(NiconicoAuthenticationState.Expired, "ログインの有効期限が切れました。設定の「ログイン」から再度ログインしてください。");
        }

        internal async Task CheckSessionAsync()
        {
            var session = loginSession.Value as NiconicoOAuthSession;
            if (disposed || session == null || IsBusy) return;
            try { await session.GetAccessTokenAsync(lifetime.Token); }
            catch (NiconicoSessionExpiredException) { InvalidateSession(session); }
            catch (Exception e) when (e is HttpRequestException || e is NiconicoLoginSessionException || e is JsonException || e is OperationCanceledException) { }
        }

        private async Task MonitorSessionAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), lifetime.Token);
                    await CheckSessionAsync();
                }
            }
            catch (OperationCanceledException) { }
        }

        private static bool IsStorageError(Exception e) => e is IOException || e is UnauthorizedAccessException || e is CryptographicException;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            (loginSession.Value as NiconicoOAuthSession)?.Invalidate();
            loginSession.Value = null;
        }
    }
}
