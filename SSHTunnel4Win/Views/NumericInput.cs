using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SSHTunnel4Win.Views;

/// <summary>
/// Attached behavior that restricts a TextBox to digits (typed or pasted).
/// </summary>
public static class NumericInput
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(NumericInput),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;
        if ((bool)e.NewValue)
        {
            box.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(box, OnPaste);
            InputMethod.SetIsInputMethodEnabled(box, false);
        }
        else
        {
            box.PreviewTextInput -= OnPreviewTextInput;
            DataObject.RemovePastingHandler(box, OnPaste);
        }
    }

    private static bool IsDigits(string text) => text.All(char.IsAsciiDigit);

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!IsDigits(e.Text)) e.Handled = true;
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !IsDigits(text.Trim()))
            e.CancelCommand();
    }
}
