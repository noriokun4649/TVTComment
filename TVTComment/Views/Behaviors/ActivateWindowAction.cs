using System.Windows;
using Microsoft.Xaml.Behaviors;

namespace TVTComment.Views.Behaviors
{
    class ActivateWindowAction : TriggerAction<Window>
    {
        protected override void Invoke(object parameter) => AssociatedObject.Activate();
    }
}
