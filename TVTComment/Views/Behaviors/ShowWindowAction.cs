using System;
using System.Windows;
using Microsoft.Xaml.Behaviors;

namespace TVTComment.Views.Behaviors
{
    class ShowWindowAction : TriggerAction<FrameworkElement>
    {
        /// <summary>
        /// 表示するウィンドウの型
        /// </summary>
        public Type WindowType
        {
            get { return (Type)GetValue(WindowTypeProperty); }
            set { SetValue(WindowTypeProperty, value); }
        }

        public static readonly DependencyProperty WindowTypeProperty =
            DependencyProperty.Register("WindowType", typeof(Type), typeof(ShowWindowAction));

        /// <summary>
        /// ウィンドウに設定するDataContext
        /// </summary>
        public object WindowDataContext
        {
            get { return GetValue(WindowDataContextProperty); }
            set { SetValue(WindowDataContextProperty, value); }
        }

        public static readonly DependencyProperty WindowDataContextProperty =
            DependencyProperty.Register("WindowDataContext", typeof(object), typeof(ShowWindowAction));

        /// <summary>
        /// モーダルとして表示するか
        /// </summary>
        public bool IsModal
        {
            get { return (bool)GetValue(IsModalProperty); }
            set { SetValue(IsModalProperty, value); }
        }

        public static readonly DependencyProperty IsModalProperty =
            DependencyProperty.Register("IsModal", typeof(bool), typeof(ShowWindowAction), new PropertyMetadata(false));

        /// <summary>
        /// ウィンドウに設定するスタイル
        /// </summary>
        public Style WindowStyle
        {
            get { return (Style)GetValue(WindowStyleProperty); }
            set { SetValue(WindowStyleProperty, value); }
        }

        public static readonly DependencyProperty WindowStyleProperty =
            DependencyProperty.Register("WindowStyle", typeof(Style), typeof(ShowWindowAction));


        /// <summary>
        /// ウィンドウの初期位置を表示要求をしたWindowの中央に表示するか
        /// </summary>
        public bool CenterOverAssociatedWindow
        {
            get { return (bool)GetValue(CenterOverAssociatedObjectProperty); }
            set { SetValue(CenterOverAssociatedObjectProperty, value); }
        }

        public static readonly DependencyProperty CenterOverAssociatedObjectProperty =
            DependencyProperty.Register("CenterOverAssociatedWindow", typeof(bool), typeof(ShowWindowAction), new PropertyMetadata(false));



        /// <summary>
        /// 同時に複数のウィンドウの表示を可能にするか
        /// </summary>
        public bool AllowMultipleWindowToShow { get; set; }

        /// <summary>
        /// 今表示しているウィンドウ
        /// <see cref="AllowMultipleWindowToShow"/>が真なら無意味
        /// </summary>
        private Window showingWindow;

        protected override void Invoke(object parameter)
        {
            if (!AllowMultipleWindowToShow && showingWindow != null)
            {
                showingWindow.Activate();
                return;
            }

            Window window = (Window)ViewModelLocator.CreateView(WindowType);
            showingWindow = window;
            window.Closed += Window_Closed;
            window.Owner = Window.GetWindow(AssociatedObject);
            if (WindowDataContext != null)
                window.DataContext = WindowDataContext;
            if (WindowStyle != null)
                window.Style = WindowStyle;

            if (CenterOverAssociatedWindow && window.Owner != null)
            {
                void sizeHandler(object sender, SizeChangedEventArgs e)
                {
                    window.SizeChanged -= sizeHandler;

                    Window ownerWindow = window.Owner;
                    if (ownerWindow.WindowState == WindowState.Minimized)
                    {
                        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        return;
                    }

                    FrameworkElement view = ownerWindow;
                    Point position = view.PointToScreen(new Point(0, 0));
                    PresentationSource source = PresentationSource.FromVisual(view);
                    position = source.CompositionTarget.TransformFromDevice.Transform(position);
                    Point middleOfView = new Point(position.X + view.ActualWidth / 2, position.Y + view.ActualHeight / 2);
                    window.Left = middleOfView.X - window.ActualWidth / 2;
                    window.Top = middleOfView.Y - window.ActualHeight / 2;
                }

                window.SizeChanged += sizeHandler;
            }

            if (IsModal)
            {
                window.ShowDialog();
            }
            else
            {
                window.Show();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            ((Window)sender).Closed -= Window_Closed;
            showingWindow = null;
        }
    }
}
