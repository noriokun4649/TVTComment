using System.Windows;
using Microsoft.Xaml.Behaviors;

namespace TVTComment.Views.Behaviors
{
    class FileDroppedTrigger : TriggerBase<FrameworkElement>
    {
        public string FilePath
        {
            get { return (string)GetValue(FilePathProperty); }
            set { SetValue(FilePathProperty, value); }
        }

        // Using a DependencyProperty as the backing store for FilePath.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty FilePathProperty =
            DependencyProperty.Register("FilePath", typeof(string), typeof(FileDroppedTrigger), new PropertyMetadata(null));


        protected override void OnAttached()
        {
            AssociatedObject.AllowDrop = true;
            AssociatedObject.PreviewDragOver += AssociatedObject_DragOver;
            AssociatedObject.Drop += AssociatedObject_Drop;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.Drop -= AssociatedObject_Drop;
            AssociatedObject.PreviewDragOver -= AssociatedObject_DragOver;
            AssociatedObject.AllowDrop = false;
        }

        private void AssociatedObject_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
            {
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
            else
                e.Effects = DragDropEffects.None;
        }


        private void AssociatedObject_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            FilePath = files[0];
            e.Handled = true;
            InvokeActions(files);
        }
    }
}
