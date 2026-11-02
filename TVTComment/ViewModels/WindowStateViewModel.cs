using ObservableUtils;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows;
using System.Windows.Input;

namespace TVTComment.ViewModels
{
    sealed class WindowStateViewModel
    {
        private readonly Func<bool> minimizeOnPanelOpen;
        private bool panelVisible;

        public ObservableValue<WindowState> State { get; } = new ObservableValue<WindowState>(WindowState.Minimized);
        public event EventHandler ActivationRequested;
        public ICommand ShowCommand { get; }
        public ICommand MinimizeCommand { get; }

        public WindowStateViewModel(Func<bool> minimizeOnPanelOpen)
        {
            this.minimizeOnPanelOpen = minimizeOnPanelOpen;
            MinimizeCommand = new RelayCommand(() => State.Value = WindowState.Minimized);
            ShowCommand = new RelayCommand(() =>
            {
                State.Value = WindowState.Normal;
                ActivationRequested?.Invoke(this, EventArgs.Empty);
            });
        }

        public void SetPanelVisible(bool visible)
        {
            if (panelVisible == visible) return;
            panelVisible = visible;
            if (visible && minimizeOnPanelOpen()) MinimizeCommand.Execute(null);
        }
    }
}
