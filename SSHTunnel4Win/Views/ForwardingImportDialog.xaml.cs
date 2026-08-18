using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Services;

namespace SSHTunnel4Win.Views;

public partial class ForwardingImportDialog : Window
{
    public List<TunnelEntry> Entries { get; private set; } = new();

    public ForwardingImportDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Auto-fill from clipboard if it holds forwarding rules
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            if (ShareService.ParseForwardingEntries(text).Count > 0)
                InputBox.Text = text;
        }
    }

    private void InputChanged(object sender, TextChangedEventArgs e)
    {
        Entries = ShareService.ParseForwardingEntries(InputBox.Text);
        PreviewList.ItemsSource = Entries
            .Select(entry => $"{entry.Type.DisplayName()}: {entry.SshArgument}")
            .ToList();
        PreviewBox.Visibility = Entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddBtn.IsEnabled = Entries.Count > 0;
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
