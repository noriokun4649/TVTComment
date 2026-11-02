using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows;
using System.Windows.Controls;

namespace TVTComment.Views
{
    /// <summary>
    /// Interaction logic for ChannelInfo.xaml
    /// </summary>
    public partial class ChannelInfoViewer : UserControl
    {
        private class ViewModel : ObservableObject
        {
            private Model.ChannelInfo channel;
            public Model.ChannelInfo Channel { get { return channel; } set { SetProperty(ref channel, value); } }

            private Model.EventInfo _event;
            public Model.EventInfo Event { get { return _event; } set { SetProperty(ref _event, value); OnPropertyChanged(nameof(EventEndTime)); } }

            public DateTime? EventEndTime { get { return Event?.StartTime + Event?.Duration; } }
        }
        public ChannelInfoViewer()
        {
            InitializeComponent();
            OuterMostGrid.DataContext = new ViewModel();
        }

        public static readonly DependencyProperty ChannelInfoProperty = DependencyProperty.Register("ChannelInfo", typeof(Model.ChannelInfo), typeof(ChannelInfoViewer),
            new PropertyMetadata(null, (sender, e) =>
            {
                (sender as ChannelInfoViewer).OnChannelInfoPropertyChanged(sender, e);
            }));
        public static readonly DependencyProperty EventInfoProperty = DependencyProperty.Register("EventInfo", typeof(Model.EventInfo), typeof(ChannelInfoViewer),
            new PropertyMetadata(null, (sender, e) =>
            {
                (sender as ChannelInfoViewer).OnEventInfoPropertyChanged(sender, e);
            }));

        public Model.ChannelInfo ChannelInfo
        {
            get { return (Model.ChannelInfo)GetValue(ChannelInfoProperty); }
            set { SetValue(ChannelInfoProperty, value); }
        }

        public Model.EventInfo EventInfo
        {
            get { return (Model.EventInfo)GetValue(EventInfoProperty); }
            set { SetValue(EventInfoProperty, value); }
        }

        private void OnChannelInfoPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            (OuterMostGrid.DataContext as ViewModel).Channel = (Model.ChannelInfo)e.NewValue;
        }
        private void OnEventInfoPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            (OuterMostGrid.DataContext as ViewModel).Event = (Model.EventInfo)e.NewValue;
        }
    }
}
