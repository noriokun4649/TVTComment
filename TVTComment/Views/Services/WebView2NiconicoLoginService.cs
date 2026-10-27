using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TVTComment.Model.NiconicoUtils;

namespace TVTComment.Views.Services
{
    sealed class WebView2NiconicoLoginService : INiconicoLoginService
    {
        public async Task<CookieCollection> LoginAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = cancellation.Token;
            using var browser = new WebView2();
            var window = new Window
            {
                Title = "ニコニコにログイン - TVTComment",
                Width = 640, Height = 800,
                Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = browser
            };
            bool closed = false;
            window.Closed += (_, _) => { closed = true; cancellation.Cancel(); };
            var completion = new TaskCompletionSource<CookieCollection>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                if (window.Owner?.WindowState == WindowState.Minimized) window.Owner.WindowState = WindowState.Normal;
                window.Show();
                var directory = Path.Combine(NiconicoSessionStore.GetDataDirectory(AppContext.BaseDirectory), "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: directory).WaitAsync(token);
                var options = environment.CreateCoreWebView2ControllerOptions();
                // ブラウザー側にはログインを残さず、検証後のセッションだけをDPAPIで保存
                options.IsInPrivateModeEnabled = true;
                options.ProfileName = "Login" + Guid.NewGuid().ToString("N");
                await browser.EnsureCoreWebView2Async(environment, options).WaitAsync(token);
                var core = browser.CoreWebView2;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") core.Navigate(e.Uri);
                };
                core.ProcessFailed += (_, _) => completion.TrySetException(new NiconicoLoginSessionException("ログイン画面が予期せず終了しました。設定の「ログイン」から再度ログインしてください。"));
                core.NavigationCompleted += async (_, e) =>
                {
                    try
                    {
                        if (closed || !e.IsSuccess || !Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
                            || uri.Scheme != "https" || !NiconicoLoginSession.IsServiceDomain(uri.Host)) return;
                        var values = await core.CookieManager.GetCookiesAsync("https://live.nicovideo.jp/");
                        token.ThrowIfCancellationRequested();
                        var cookies = new CookieCollection();
                        foreach (var value in values)
                        {
                            var cookie = value.ToSystemNetCookie();
                            if (NiconicoLoginSession.IsServiceDomain(cookie.Domain) && !cookie.Expired) cookies.Add(cookie);
                        }
                        if (cookies["user_session"] != null) completion.TrySetResult(cookies);
                    }
                    catch (OperationCanceledException) { completion.TrySetCanceled(); }
                    catch (Exception error) { completion.TrySetException(new NiconicoLoginSessionException("ログイン情報を取得できませんでした。設定の「ログイン」から再度ログインしてください。", error)); }
                };
                core.Navigate("https://account.nicovideo.jp/login?site=niconico&next_url=https%3A%2F%2Flive.nicovideo.jp%2F");
                return await completion.Task.WaitAsync(token);
            }
            catch (WebView2RuntimeNotFoundException e)
            {
                throw new NiconicoLoginSessionException("ログイン画面に必要な部品（Microsoft Edge WebView2 Runtime）が見つかりません。インストール後、設定の「ログイン」から再度ログインしてください。", e);
            }
            catch (Exception e) when (e is COMException || e is InvalidOperationException)
            {
                throw new NiconicoLoginSessionException("ログイン画面を開けませんでした。アプリを再起動して、設定の「ログイン」から再度ログインしてください。", e);
            }
            finally
            {
                if (!closed) window.Close();
            }
        }
    }
}
