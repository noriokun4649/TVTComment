using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TVTComment.Views.AttachedProperties
{
    public static class PreserveHorizontalScrollOffset
    {
        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(PreserveHorizontalScrollOffset), new PropertyMetadata(false, IsEnabledChanged));

        private static void IsEnabledChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            if (obj is not ListBoxItem item) return;

            if ((bool)e.OldValue) item.RequestBringIntoView -= Item_RequestBringIntoView;
            if ((bool)e.NewValue) item.RequestBringIntoView += Item_RequestBringIntoView;
        }

        private static void Item_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            var item = (ListBoxItem)sender;
            if (e.TargetObject != item || !e.TargetRect.IsEmpty) return;

            DependencyObject parent = VisualTreeHelper.GetParent(item);
            while (parent != null && parent is not ScrollContentPresenter)
                parent = VisualTreeHelper.GetParent(parent);
            if (parent is not ScrollContentPresenter viewport) return;

            double left = Math.Max(0, viewport.TranslatePoint(new Point(0, 0), item).X);
            if (left >= item.ActualWidth) return;

            // Keep the horizontal request inside the viewport while allowing vertical scrolling.
            e.Handled = true;
            item.BringIntoView(new Rect(left, 0, Math.Min(1, item.ActualWidth - left), item.ActualHeight));
        }
    }
}
