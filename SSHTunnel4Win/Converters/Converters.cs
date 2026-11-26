using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;

namespace SSHTunnel4Win.Converters;

public class ConnectionStateToColorConverter : IValueConverter
{
    // Mid-tone colors that stay visible on both white and selection-highlight backgrounds
    private static readonly Brush DisconnectedBrush = Freeze(Color.FromRgb(0x8A, 0x8A, 0x8A));
    private static readonly Brush ConnectingBrush = Freeze(Color.FromRgb(0xE0, 0x8E, 0x00));
    private static readonly Brush ConnectedBrush = Freeze(Color.FromRgb(0x10, 0x8A, 0x3C));
    private static readonly Brush ErrorBrush = Freeze(Color.FromRgb(0xD1, 0x34, 0x38));

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Brush For(ConnectionState state) => state switch
    {
        ConnectionState.Connecting => ConnectingBrush,
        ConnectionState.Connected => ConnectedBrush,
        ConnectionState.Error => ErrorBrush,
        _ => DisconnectedBrush
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ConnectionState state ? For(state) : DisconnectedBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class ConnectionStateToLabelConverter : IValueConverter
{
    public static string For(ConnectionState state) => state switch
    {
        ConnectionState.Disconnected => Strings.Disconnected,
        ConnectionState.Connecting => Strings.Connecting,
        ConnectionState.Connected => Strings.Connected,
        ConnectionState.Error => Strings.Error,
        _ => ""
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ConnectionState state ? For(state) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class AuthMethodToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AuthMethod method && parameter is string target)
            return method.ToString() == target;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string target)
            return Enum.Parse<AuthMethod>(target);
        return Binding.DoNothing;
    }
}

public class TunnelTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TunnelType.Dynamic ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the string is empty; used for TextBox placeholder overlays.</summary>
public class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
