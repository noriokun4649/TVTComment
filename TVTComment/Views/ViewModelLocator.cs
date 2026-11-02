using Microsoft.Extensions.DependencyInjection;
using System;
using System.ComponentModel;
using System.Windows;

namespace TVTComment.Views
{
    static class ViewModelLocator
    {
        private static IServiceProvider services;

        public static void Configure(IServiceProvider serviceProvider) => services = serviceProvider;

        public static FrameworkElement CreateView(Type viewType) =>
            (FrameworkElement)ActivatorUtilities.CreateInstance(services, viewType);

        public static bool GetAutoWireViewModel(DependencyObject obj) => (bool)obj.GetValue(AutoWireViewModelProperty);
        public static void SetAutoWireViewModel(DependencyObject obj, bool value) => obj.SetValue(AutoWireViewModelProperty, value);

        public static readonly DependencyProperty AutoWireViewModelProperty = DependencyProperty.RegisterAttached(
            "AutoWireViewModel", typeof(bool), typeof(ViewModelLocator), new PropertyMetadata(false, AutoWireViewModelChanged));

        private static void AutoWireViewModelChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue || DesignerProperties.GetIsInDesignMode(obj)) return;

            var view = (FrameworkElement)obj;
            Type viewType = view.GetType();
            Type viewModelType = viewType.Assembly.GetType(viewType.FullName.Replace(".Views.", ".ViewModels.") + "ViewModel", true);
            // ViewModels are owned and disposed by their views, not by the root DI container.
            view.DataContext = ActivatorUtilities.CreateInstance(services, viewModelType);
        }
    }
}
