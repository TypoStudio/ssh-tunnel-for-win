using System.Linq;
using System.Windows;
using SSHTunnel4Win.Resources;

namespace SSHTunnel4Win.Views;

/// <summary>
/// Message boxes owned by the active window, so they never open behind the app.
/// </summary>
public static class Prompt
{
    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons,
        MessageBoxImage image, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var owner = Owner;
        return owner != null
            ? MessageBox.Show(owner, message, title, buttons, image, defaultResult)
            : MessageBox.Show(message, title, buttons, image, defaultResult);
    }

    /// <summary>Asks a yes/no question with "No" as the default so Enter never confirms by accident.</summary>
    public static bool Confirm(string message, string? title = null, bool warning = true) =>
        Show(message, title ?? Strings.SSHTunnelManager, MessageBoxButton.YesNo,
            warning ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>Asks a yes/no question where "Yes" is the expected answer.</summary>
    public static bool Ask(string message, string? title = null) =>
        Show(message, title ?? Strings.SSHTunnelManager, MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes;

    public static void Info(string message, string? title = null) =>
        Show(message, title ?? Strings.SSHTunnelManager, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warn(string message, string? title = null) =>
        Show(message, title ?? Strings.SSHTunnelManager, MessageBoxButton.OK, MessageBoxImage.Warning);
}
