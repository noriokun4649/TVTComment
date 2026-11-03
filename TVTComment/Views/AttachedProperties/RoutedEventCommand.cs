using System.Windows;
using System.Windows.Input;

namespace TVTComment.Views.AttachedProperties
{
    public static class RoutedEventCommand
    {
        public static RoutedEvent GetEvent(DependencyObject obj) => (RoutedEvent)obj.GetValue(EventProperty);
        public static void SetEvent(DependencyObject obj, RoutedEvent value) => obj.SetValue(EventProperty, value);

        public static readonly DependencyProperty EventProperty = DependencyProperty.RegisterAttached(
            "Event", typeof(RoutedEvent), typeof(RoutedEventCommand), new PropertyMetadata(null, EventChanged));

        public static ICommand GetCommand(DependencyObject obj) => (ICommand)obj.GetValue(CommandProperty);
        public static void SetCommand(DependencyObject obj, ICommand value) => obj.SetValue(CommandProperty, value);

        public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
            "Command", typeof(ICommand), typeof(RoutedEventCommand));

        public static object GetCommandParameter(DependencyObject obj) => obj.GetValue(CommandParameterProperty);
        public static void SetCommandParameter(DependencyObject obj, object value) => obj.SetValue(CommandParameterProperty, value);

        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
            "CommandParameter", typeof(object), typeof(RoutedEventCommand));

        private static void EventChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            if (obj is not UIElement element) return;

            if (e.OldValue is RoutedEvent oldEvent) element.RemoveHandler(oldEvent, new RoutedEventHandler(ExecuteCommand));
            if (e.NewValue is RoutedEvent newEvent) element.AddHandler(newEvent, new RoutedEventHandler(ExecuteCommand));
        }

        private static void ExecuteCommand(object sender, RoutedEventArgs e)
        {
            var element = (DependencyObject)sender;
            var command = GetCommand(element);
            var parameter = GetCommandParameter(element);
            if (command?.CanExecute(parameter) == true) command.Execute(parameter);
        }
    }
}
