using System.Windows;
using System.Windows.Controls;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public partial class ImportDialog : Window
{
    private readonly MainViewModel _vm;

    public ImportDialog(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Auto-fill from clipboard if it contains a share string
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            if (text.StartsWith("sshtunnel://"))
            {
                InputBox.Text = text;
            }
        }
    }

    private void InputChanged(object sender, TextChangedEventArgs e)
    {
        var config = ShareService.Decode(InputBox.Text);
        if (config != null)
        {
            PreviewBox.Visibility = Visibility.Visible;
            PreviewName.Text = $"{Strings.Name}: {(string.IsNullOrEmpty(config.Name) ? "-" : config.Name)}";
            PreviewHost.Text = $"{Strings.Host}: {config.Username}@{config.Host}:{config.Port}";
            PreviewTunnels.Text = $"{Strings.Tunnels}: {config.Tunnels.Count} {Strings.TunnelRules}";
            ImportBtn.IsEnabled = true;
        }
        else
        {
            PreviewBox.Visibility = Visibility.Collapsed;
            ImportBtn.IsEnabled = false;
        }
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        _vm.ImportFromShareString(InputBox.Text);
        DialogResult = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
