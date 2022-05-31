using Microsoft.Web.WebView2.Core;
using Prism.Mvvm;
using Reactive.Bindings;
using System.Linq;
using System.Windows;

namespace TVTComment.ViewModels
{
    class NiconicoOAuthWindowViewModel : BindableBase
    {
        public ReactiveCommand<CoreWebView2HttpResponseHeaders> RequestRecived { get; } = new ReactiveCommand<CoreWebView2HttpResponseHeaders>();

        private readonly Model.ChatService.NiconicoChatService niconico;

        public NiconicoOAuthWindowViewModel(Model.TVTComment model)
        {
            niconico = model.ChatServices.OfType<Model.ChatService.NiconicoChatService>().Single();
            RequestRecived.WithSubscribe(resp => OnRequestRecived(resp));
        }

        private async void OnRequestRecived(CoreWebView2HttpResponseHeaders resp)
        {
            var token = resp.GetHeader("TvTCmt-Server-AccessToken");
            var scorp = resp.GetHeader("TvTCmt-Server-Scope");

            try
            {
                await niconico.SetToken(token);
            }
            catch (Model.NiconicoUtils.NiconicoLoginSessionException)
            {
                MessageBox.Show("ニコニコへのアクセス認可に失敗しました");
            }
        }
    }
}
