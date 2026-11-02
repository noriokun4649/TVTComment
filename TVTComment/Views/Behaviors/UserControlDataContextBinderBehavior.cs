using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace TVTComment.Views.Behaviors
{
    /// <summary>
    /// AutoWireViewModelがセットされたUserControlのDataContextに、UserControlが配置されたWindow側からアクセスするために使う
    /// </summary>
    class UserControlDataContextBinderBehavior : Behavior<FrameworkElement>
    {
        public object Binding
        {
            get { return GetValue(BindingProperty); }
            set { SetValue(BindingProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Binding.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty BindingProperty =
            DependencyProperty.Register("Binding", typeof(object), typeof(UserControlDataContextBinderBehavior), new PropertyMetadata(null));

        private UserControl target;

        protected override void OnAttached()
        {
            target = GetDescendantOrSelfUserControl(AssociatedObject);
            if (target == null) return;

            OnDataContextChanged(target.DataContext);
            target.DataContextChanged += AssociatedObject_DataContextChanged;
        }

        protected override void OnDetaching()
        {
            if (target == null) return;
            target.DataContextChanged -= AssociatedObject_DataContextChanged;
        }

        private UserControl GetDescendantOrSelfUserControl(FrameworkElement frameworkElement)
        {
            if (frameworkElement as UserControl != null) return frameworkElement as UserControl;

            foreach (var child in LogicalTreeHelper.GetChildren(frameworkElement))
            {
                if (child is FrameworkElement elem)
                {
                    var descendant = GetDescendantOrSelfUserControl(elem);
                    if (descendant != null) return descendant;
                }
            }
            return null;
        }

        private void AssociatedObject_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            OnDataContextChanged(e.NewValue);
        }

        private void OnDataContextChanged(object newDataContext)
        {
            Binding = newDataContext;
        }
    }
}
