using ObservableUtils;
using Prism.Commands;
using Prism.Interactivity.InteractionRequest;
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
        public InteractionRequest<Notification> ActivationRequest { get; } = new InteractionRequest<Notification>();
        public ICommand ShowCommand { get; }
        public ICommand MinimizeCommand { get; }

        public WindowStateViewModel(Func<bool> minimizeOnPanelOpen)
        {
            this.minimizeOnPanelOpen = minimizeOnPanelOpen;
            MinimizeCommand = new DelegateCommand(() => State.Value = WindowState.Minimized);
            ShowCommand = new DelegateCommand(() =>
            {
                State.Value = WindowState.Normal;
                ActivationRequest.Raise(new Notification());
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
