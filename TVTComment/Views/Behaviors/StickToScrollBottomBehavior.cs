using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Xaml.Behaviors;
using System.Windows.Media;

namespace TVTComment.Views.Behaviors
{
    /// <summary>
    /// <see cref="ListBox"/>などでスクロール位置が一番下にくっつくようにする
    /// ログなどの表示を想定している
    /// </summary>
    /// <remarks>
    /// アイテムが選択されるとくっつくのをやめる
    /// いったん上にスクロールしてから一番下にスクロールすると再びくっつくようになる
    /// </remarks>
    public class StickToScrollBottomBehavior : Behavior<Selector>
    {
        private ScrollViewer scrollViewer;
        private bool isSticking = true;
        private bool lastIsAtBottom = false;

        protected override void OnAttached()
        {
            AssociatedObject.Loaded += OnLoaded;
            AssociatedObject.Unloaded += OnUnLoaded;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.Loaded -= OnLoaded;
            AssociatedObject.Unloaded -= OnUnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AssociatedObject.SelectionChanged += AssociatedObject_SelectionChanged;

            if (VisualTreeHelper.GetChildrenCount(AssociatedObject) > 0)
            {
                var border = VisualTreeHelper.GetChild(AssociatedObject, 0);
                scrollViewer = (ScrollViewer)VisualTreeHelper.GetChild(border, 0);
                scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;

                if (isSticking)
                    scrollViewer.ScrollToBottom();
            }
        }

        private void OnUnLoaded(object sender, RoutedEventArgs e)
        {
            if (scrollViewer != null)//なぜかヌルポで落ちたことがあったから
                scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            AssociatedObject.SelectionChanged -= AssociatedObject_SelectionChanged;
            scrollViewer = null;
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (isSticking)
                scrollViewer.ScrollToBottom();

            bool isAtBottom = Math.Abs(scrollViewer.ExtentHeight - (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight)) <= 2.0;
            if (!lastIsAtBottom && isAtBottom)
                isSticking = true;
            lastIsAtBottom = isAtBottom;
        }

        private void AssociatedObject_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AssociatedObject.SelectedIndex == -1)
            {
                isSticking = true;
            }
            else
            {
                isSticking = false;
            }
        }
    }
}
