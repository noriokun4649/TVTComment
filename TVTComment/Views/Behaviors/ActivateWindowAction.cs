using System.Windows;
using System.Windows.Interactivity;

namespace TVTComment.Views.Behaviors
{
    class ActivateWindowAction : TriggerAction<Window>
    {
        protected override void Invoke(object parameter) => AssociatedObject.Activate();
    }
}
