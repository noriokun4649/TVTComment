using System.Windows;
using TVTComment.ViewModels.Notifications;

namespace TVTComment.Views
{
    public partial class NotificationWindow : Window
    {
        public NotificationWindow()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => Close();

        private void DetailsExpander_Expanded(object sender, RoutedEventArgs e)
        {
            SetCurrentValue(SizeToContentProperty, SizeToContent.Manual);
            if (WindowState == WindowState.Normal && ActualHeight < 500)
                Height = 500;
        }

        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is Notification notification)
                Clipboard.SetText(notification.Content?.ToString() ?? "");
        }
    }
}
