using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public partial class ImportDialog : Window
{
    private readonly MainViewModel _vm;

    public ImportDialog(MainViewModel vm, string? initialText = null)
    {
        _vm = vm;
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(initialText))
            InputBox.Text = initialText;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Auto-fill from clipboard if it contains a share string
        if (string.IsNullOrEmpty(InputBox.Text))
        {
            var text = ClipboardHelper.TryGetText();
            if (text.StartsWith("sshtunnel://") || text.StartsWith("ssh "))
                InputBox.Text = text;
        }
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private void InputChanged(object sender, TextChangedEventArgs e)
    {
        var hasInput = !string.IsNullOrWhiteSpace(InputBox.Text);
        var config = hasInput ? ShareService.Decode(InputBox.Text) : null;
        if (config != null)
        {
            PreviewBox.Visibility = Visibility.Visible;
            PreviewName.Text = string.IsNullOrEmpty(config.Name) ? config.Host : config.Name;
            var user = string.IsNullOrEmpty(config.Username) ? "" : config.Username + "@";
            PreviewHost.Text = $"{Strings.Host}: {user}{config.Host}:{config.Port}";
            PreviewTunnels.Text = config.Tunnels.Count == 0
                ? $"{Strings.PortForwarding}: -"
                : $"{Strings.PortForwarding}: " + string.Join(", ",
                    config.Tunnels.Select(t => $"{t.Type.Flag()} {t.SshArgument}"));
        }
        else
        {
            PreviewBox.Visibility = Visibility.Collapsed;
        }
        InvalidText.Visibility = hasInput && config == null ? Visibility.Visible : Visibility.Collapsed;
        ImportBtn.IsEnabled = config != null;
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        if (_vm.ImportFromShareString(InputBox.Text) == null) return;
        DialogResult = true;
    }
}
