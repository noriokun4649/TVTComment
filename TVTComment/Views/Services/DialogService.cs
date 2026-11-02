using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TVTComment.ViewModels;
using TVTComment.ViewModels.Notifications;
using TVTComment.ViewModels.Services;
using TVTComment.ViewModels.SettingsWindowContents;

namespace TVTComment.Views.Services
{
    sealed class DialogService : IDialogService
    {
        private ChatCollectServiceCreationSettingsControl creationSettings;
        private ChatCollectServiceCreationPresetControl creationPreset;

        public void ShowNotification(Notification notification)
        {
            var window = new NotificationWindow { DataContext = notification };
            SetOwner(window);
            window.ShowDialog();
        }

        public Task<ChatCollectServiceCreationSettingsConfirmation> ShowAsync(ChatCollectServiceCreationSettingsConfirmation confirmation)
        {
            creationSettings ??= (ChatCollectServiceCreationSettingsControl)ViewModelLocator.CreateView(typeof(ChatCollectServiceCreationSettingsControl));
            var viewModel = (ChatCollectServiceCreationSettingsControlViewModel)creationSettings.DataContext;
            viewModel.Initialize(confirmation);
            Show(creationSettings, viewModel, confirmation.Title, WindowStyle.ToolWindow);
            return Task.FromResult(confirmation);
        }

        public Task<ChatCollectServiceCreationPresetConfirmation> ShowAsync(ChatCollectServiceCreationPresetConfirmation confirmation)
        {
            creationPreset ??= (ChatCollectServiceCreationPresetControl)ViewModelLocator.CreateView(typeof(ChatCollectServiceCreationPresetControl));
            var viewModel = (ChatCollectServiceCreationPresetControlViewModel)creationPreset.DataContext;
            viewModel.Initialize(confirmation);
            Show(creationPreset, viewModel, confirmation.Title, WindowStyle.SingleBorderWindow);
            return Task.FromResult(confirmation);
        }

        private static void Show(UserControl content, IDialogViewModel viewModel, string title, WindowStyle style)
        {
            var window = new Window { Content = content, Title = title, Width = 500, Height = 400, WindowStyle = style };
            SetOwner(window);
            Action close = window.Close;
            viewModel.CloseRequested += close;
            try
            {
                window.ShowDialog();
            }
            finally
            {
                viewModel.CloseRequested -= close;
                window.Content = null;
            }
        }

        private static void SetOwner(Window window)
        {
            Window owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? Application.Current.MainWindow;
            if (owner != null && owner.IsVisible)
                window.Owner = owner;
            window.WindowStartupLocation = window.Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        }
    }
}
