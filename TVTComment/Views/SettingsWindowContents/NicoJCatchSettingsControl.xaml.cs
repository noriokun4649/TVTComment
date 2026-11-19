using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace TVTComment.Views.SettingsWindowContents
{
    public partial class NicoJCatchSettingsControl : UserControl
    {
        public NicoJCatchSettingsControl() => InitializeComponent();

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "NicojCatch過去ログの親フォルダ", InitialDirectory = Folder.Text };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                Folder.SetCurrentValue(TextBox.TextProperty, dialog.FolderName);
        }
    }
}
