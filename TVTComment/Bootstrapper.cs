using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using TVTComment.ViewModels.Services;
using TVTComment.Views;

namespace TVTComment
{
    class Bootstrapper
    {
        public void Run()
        {
            var services = new ServiceCollection();
            services.AddSingleton(new Model.TVTComment());
            services.AddTransient<Model.NiconicoUtils.INiconicoLoginService, Views.Services.WebView2NiconicoLoginService>();
            services.AddTransient<IDialogService, Views.Services.DialogService>();
            services.AddTransient<ChatCollectServiceCreationOptionFactory>();

            ViewModelLocator.Configure(services.BuildServiceProvider());
            ((Window)ViewModelLocator.CreateView(typeof(Shell))).Show();
        }
    }
}
