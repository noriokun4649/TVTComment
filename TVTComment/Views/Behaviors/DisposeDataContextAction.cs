using System;
using System.Windows;
using Microsoft.Xaml.Behaviors;

namespace TVTComment.Views.Behaviors
{
    class DisposeDataContextAction : TriggerAction<FrameworkElement>
    {
        protected override void Invoke(object parameter)
        {
            (AssociatedObject.DataContext as IDisposable)?.Dispose();
        }
    }
}
