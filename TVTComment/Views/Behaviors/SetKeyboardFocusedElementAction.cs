using System.Windows;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace TVTComment.Views.Behaviors
{
    class SetKeyboardFocusedElementAction : TriggerAction<DependencyObject>
    {
        public IInputElement Element
        {
            get { return (IInputElement)GetValue(ElementProperty); }
            set { SetValue(ElementProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Element.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ElementProperty =
            DependencyProperty.Register("Element", typeof(IInputElement), typeof(SetKeyboardFocusedElementAction));

        protected override void Invoke(object parameter)
        {
            if (Element != null)
                Keyboard.Focus(Element);
        }
    }
}
