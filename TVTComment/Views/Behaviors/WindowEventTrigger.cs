using System.Windows;
using Microsoft.Xaml.Behaviors;

namespace TVTComment.Views.Behaviors
{
    class WindowEventTrigger : EventTriggerBase<FrameworkElement>
    {
        private Window window;

        public string EventName
        {
            get { return (string)GetValue(EventNameProperty); }
            set { SetValue(EventNameProperty, value); }
        }

        // Using a DependencyProperty as the backing store for EventName.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty EventNameProperty =
            DependencyProperty.Register("EventName", typeof(string), typeof(WindowEventTrigger), new PropertyMetadata("Loaded"));



        protected override void OnAttached()
        {
            if (AssociatedObject is not FrameworkElement element) return;
            element.Loaded += AssociatedObject_Loaded;
        }

        private void AssociatedObject_Loaded(object sender, RoutedEventArgs e)
        {
            ((FrameworkElement)AssociatedObject).Loaded -= AssociatedObject_Loaded;

            window = Window.GetWindow(AssociatedObject);
            SourceObject = window;
        }

        protected override string GetEventName()
        {
            return EventName;
        }
    }
}
