using Microsoft.Practices.Unity;
using Prism.Unity;
using System.Windows;
using TVTComment.Views;

namespace TVTComment
{
    class Bootstrapper : UnityBootstrapper
    {
        protected override DependencyObject CreateShell()
        {
            return Container.Resolve<Shell>();
        }

        protected override void InitializeShell()
        {
            ((Window)Shell).Show();
        }

        protected override void ConfigureContainer()
        {
            base.ConfigureContainer();

            Container.RegisterInstance(typeof(Model.TVTComment), new Model.TVTComment());
            Container.RegisterType<Model.NiconicoUtils.INiconicoLoginService, Views.Services.WebView2NiconicoLoginService>();
            Container.RegisterTypeForNavigation<Views.ChatCollectServiceCreationOptionControl.NichanChatCollectServiceCreationOptionControl>
                 (nameof(Views.ChatCollectServiceCreationOptionControl.NichanChatCollectServiceCreationOptionControl));
            Container.RegisterTypeForNavigation<Views.ChatCollectServiceCreationOptionControl.FileChatCollectServiceCreationOptionControl>
                (nameof(Views.ChatCollectServiceCreationOptionControl.FileChatCollectServiceCreationOptionControl));
            Container.RegisterTypeForNavigation<Views.ChatCollectServiceCreationOptionControl.NiconicoLiveChatCollectServiceCreationOptionControl>
                (nameof(Views.ChatCollectServiceCreationOptionControl.NiconicoLiveChatCollectServiceCreationOptionControl));
            Container.RegisterTypeForNavigation<Views.ChatCollectServiceCreationOptionControl.TwitterLiveChatCollectServiceCreationOptionControl>
                (nameof(Views.ChatCollectServiceCreationOptionControl.TwitterLiveChatCollectServiceCreationOptionControl));
        }
    }
}
