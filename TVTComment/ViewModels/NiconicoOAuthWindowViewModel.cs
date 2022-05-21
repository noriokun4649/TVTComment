using Microsoft.Web.WebView2.Core;
using Prism.Mvvm;
using Reactive.Bindings;
using System.Windows;

namespace TVTComment.ViewModels
{
    class NiconicoOAuthWindowViewModel : BindableBase
    {
        public ReactiveCommand<CoreWebView2HttpResponseHeaders> RequestRecived { get; } = new ReactiveCommand<CoreWebView2HttpResponseHeaders>();

        public NiconicoOAuthWindowViewModel(Model.TVTComment model)
        {
            RequestRecived.WithSubscribe(resp => OnRequestRecived(resp));
        }

        private void OnRequestRecived(CoreWebView2HttpResponseHeaders resp)
        {
            var token = resp.GetHeader("TvTCmt-Server-AccessToken");
            var scorp = resp.GetHeader("TvTCmt-Server-Scope");

            MessageBox.Show(token+scorp);

        }
    }
}
