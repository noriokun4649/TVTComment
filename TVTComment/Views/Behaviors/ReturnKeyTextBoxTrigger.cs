using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace TVTComment.Views.Behaviors
{
    /// <summary>
    /// <see cref="TextBox"/>のReturnキーを<see cref="Microsoft.Xaml.Behaviors.TriggerAction"/>の実行に割り当て、改行を<see cref="ModifierKey"/>+Returnにする
    /// </summary>
    class ReturnKeyTextBoxTrigger : TriggerBase<TextBox>
    {
        public ModifierKeys ModifierKey
        {
            get { return (ModifierKeys)GetValue(ModifierKeyProperty); }
            set { SetValue(ModifierKeyProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ModifierKey.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ModifierKeyProperty =
            DependencyProperty.Register("ModifierKey", typeof(ModifierKeys), typeof(ReturnKeyTextBoxTrigger), new PropertyMetadata(ModifierKeys.Alt));


        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.PreviewKeyDown += AssociatedObject_PreviewKeyDown;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.PreviewKeyDown -= AssociatedObject_PreviewKeyDown;
            base.OnDetaching();
        }

        private void AssociatedObject_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            //AltとかといっしょにEnterを押すとEnterはSystemKey扱いになるっぽい
            if (Keyboard.Modifiers == ModifierKey && (e.SystemKey == Key.Enter || e.Key == Key.Enter))
            {
                int idx = AssociatedObject.CaretIndex;
                AssociatedObject.Text = AssociatedObject.Text.Insert(AssociatedObject.CaretIndex, "\n");
                AssociatedObject.CaretIndex = idx + 1;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                var textBinding = BindingOperations.GetBindingExpression(AssociatedObject, TextBox.TextProperty);
                if (textBinding != null)
                    textBinding.UpdateSource();
                InvokeActions(null);

                e.Handled = true;
            }
        }
    }
}
