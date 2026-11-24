using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public partial class TunnelDetailView : UserControl
{
    private TunnelDetailViewModel _vm = null!;
    private MainViewModel _mainVm = null!;

    public TunnelDetailView()
    {
        InitializeComponent();
    }

    public void Initialize(TunnelDetailViewModel vm, MainViewModel mainVm)
    {
        _vm = vm;
        _mainVm = mainVm;
        DataContext = vm;

        UpdateAuthVisibility();
        UpdateConnectLabel();
        UpdateError();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TunnelDetailViewModel.AuthMethod))
                UpdateAuthVisibility();
            if (e.PropertyName is nameof(TunnelDetailViewModel.State) or nameof(TunnelDetailViewModel.ErrorMessage))
            {
                UpdateConnectLabel();
                UpdateError();
            }
        };

        // Set initial password
        if (!string.IsNullOrEmpty(vm.Password))
            PasswordBox.Password = vm.Password;
    }

    private void UpdateAuthVisibility()
    {
        var identity = _vm.AuthMethod == AuthMethod.IdentityFile;
        IdentityFilePanel.Visibility = identity ? Visibility.Visible : Visibility.Collapsed;
        PassphraseLabel.Visibility = identity ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(PasswordBox, identity ? Strings.KeyPassphrase : Strings.Password);
    }

    private void UpdateConnectLabel()
    {
        var active = _vm.State.IsActive();
        ConnectLabel.Text = active ? Strings.Disconnect : Strings.Connect;
        ConnectGlyph.Text = active ? "\uE71A" : "\uE768"; // Stop / Play
        AutomationProperties.SetName(ConnectButton, ConnectLabel.Text);
    }

    private void UpdateError()
    {
        ErrorText.Text = _vm.ErrorMessage;
        ErrorBanner.Visibility = string.IsNullOrEmpty(_vm.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
            _vm.Password = PasswordBox.Password;
    }

    private void LoadFromSSHConfig(object sender, RoutedEventArgs e)
    {
        var hosts = SSHConfigParser.Parse();
        if (hosts.Count == 0)
        {
            Prompt.Info(Strings.NoHostsInConfig, Strings.LoadFromSSHConfig);
            return;
        }

        var picker = new SSHConfigPickerDialog(hosts) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true && picker.SelectedHost != null)
            _vm.ApplySSHConfig(picker.SelectedHost);
    }

    private void ShowLog(object sender, RoutedEventArgs e) =>
        LogDialog.ShowFor(_vm, _mainVm.ProcessManager, Window.GetWindow(this));
}

// SSH Config Picker Dialog
public partial class SSHConfigPickerDialog : Window
{
    public SSHConfigHost? SelectedHost { get; private set; }
    private readonly List<SSHConfigHost> _allHosts;
    private readonly ListBox _listBox;
    private readonly Button _loadBtn;

    public SSHConfigPickerDialog(List<SSHConfigHost> hosts)
    {
        _allHosts = hosts;
        Title = Strings.LoadFromSSHConfig;
        Width = 420;
        Height = 400;
        MinWidth = 320;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16) };

        // Search box with placeholder
        var searchBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(2, 3, 2, 3) };
        AutomationProperties.SetName(searchBox, Strings.Search);
        var placeholder = new TextBlock { Text = Strings.Search, Style = (Style)Application.Current.FindResource("PlaceholderText") };
        var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        searchGrid.Children.Add(searchBox);
        searchGrid.Children.Add(placeholder);
        DockPanel.SetDock(searchGrid, Dock.Top);
        root.Children.Add(searchGrid);

        // Buttons
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var buttonStyle = (Style)Application.Current.FindResource("DialogButton");
        _loadBtn = new Button { Content = Strings.Load, IsDefault = true, IsEnabled = false, Style = buttonStyle, Margin = new Thickness(0, 0, 8, 0) };
        _loadBtn.Click += (_, _) => Accept();
        var cancelBtn = new Button { Content = Strings.Cancel, IsCancel = true, Style = buttonStyle };
        btnPanel.Children.Add(_loadBtn);
        btnPanel.Children.Add(cancelBtn);
        DockPanel.SetDock(btnPanel, Dock.Bottom);
        root.Children.Add(btnPanel);

        _listBox = new ListBox { DisplayMemberPath = "DisplayText" };
        AutomationProperties.SetName(_listBox, Strings.LoadFromSSHConfig);
        root.Children.Add(_listBox);

        RefreshList("");

        searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            RefreshList(searchBox.Text);
        };
        // Arrow keys in the search box move through the results so the mouse is never required
        searchBox.PreviewKeyDown += (_, e) =>
        {
            if (_listBox.Items.Count == 0) return;
            if (e.Key == Key.Down)
            {
                _listBox.SelectedIndex = Math.Min(_listBox.SelectedIndex + 1, _listBox.Items.Count - 1);
                _listBox.ScrollIntoView(_listBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                _listBox.SelectedIndex = Math.Max(_listBox.SelectedIndex - 1, 0);
                _listBox.ScrollIntoView(_listBox.SelectedItem);
                e.Handled = true;
            }
        };
        _listBox.SelectionChanged += (_, _) => _loadBtn.IsEnabled = _listBox.SelectedItem != null;
        _listBox.MouseDoubleClick += (_, _) => Accept();

        Content = root;
        Loaded += (_, _) => searchBox.Focus();
    }

    private void Accept()
    {
        if (_listBox.SelectedItem is not HostItem item) return;
        SelectedHost = item.Host;
        DialogResult = true;
    }

    private void RefreshList(string query)
    {
        var lower = query.ToLowerInvariant();
        var filtered = string.IsNullOrEmpty(lower)
            ? _allHosts
            : _allHosts.FindAll(h =>
                h.Name.ToLowerInvariant().Contains(lower) ||
                h.Hostname.ToLowerInvariant().Contains(lower) ||
                h.User.ToLowerInvariant().Contains(lower));
        _listBox.ItemsSource = filtered.ConvertAll(h => new HostItem(h));
        if (filtered.Count > 0) _listBox.SelectedIndex = 0;
    }

    private class HostItem
    {
        public SSHConfigHost Host { get; }
        public string DisplayText => $"{Host.Name}  ({(string.IsNullOrEmpty(Host.User) ? "" : $"{Host.User}@")}{Host.Hostname}{(Host.Port != 22 ? $":{Host.Port}" : "")})";
        public HostItem(SSHConfigHost host) => Host = host;
    }
}

// Log Dialog (non-modal, real-time). One window per tunnel; asking again brings it forward.
public class LogDialog : Window
{
    private static readonly Dictionary<Guid, LogDialog> OpenDialogs = new();

    private readonly TunnelDetailViewModel _vm;
    private readonly SSHProcessManager _processManager;
    private readonly TextBox _logBox;
    private readonly TextBlock _emptyText;
    private readonly TextBlock _copyStatus;

    public static void ShowFor(TunnelDetailViewModel vm, SSHProcessManager processManager, Window? owner)
    {
        if (OpenDialogs.TryGetValue(vm.ConfigId, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var dialog = new LogDialog(vm, processManager) { Owner = owner };
        OpenDialogs[vm.ConfigId] = dialog;
        dialog.Show();
    }

    private LogDialog(TunnelDetailViewModel vm, SSHProcessManager processManager)
    {
        _vm = vm;
        _processManager = processManager;
        Title = Strings.ConnectionLog + " - " + (string.IsNullOrEmpty(vm.Name) ? vm.Host : vm.Name);
        Width = 640;
        Height = 460;
        MinWidth = 360;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _logBox = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12
        };

        var panel = new DockPanel { Margin = new Thickness(16) };
        var buttonStyle = (Style)Application.Current.FindResource("DialogButton");

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(toolbar, Dock.Top);
        var copyBtn = new Button { Content = Strings.CopyLog, Style = buttonStyle, Margin = new Thickness(0, 0, 8, 0) };
        copyBtn.Click += (_, _) => ShowCopyResult(ClipboardHelper.TrySetText(_logBox.Text));
        toolbar.Children.Add(copyBtn);
        var clearBtn = new Button { Content = Strings.ClearLog, Style = buttonStyle, Margin = new Thickness(0, 0, 8, 0) };
        // Clear the stored log, not just the view, so it doesn't reappear with the next line
        clearBtn.Click += (_, _) => _processManager.ClearLog(_vm.ConfigId);
        toolbar.Children.Add(clearBtn);
        _copyStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        toolbar.Children.Add(_copyStatus);
        panel.Children.Add(toolbar);

        var closeBtn = new Button { Content = Strings.Close, IsCancel = true, Style = buttonStyle, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(closeBtn, Dock.Bottom);
        closeBtn.Click += (_, _) => Close();
        panel.Children.Add(closeBtn);

        _emptyText = new TextBlock
        {
            Text = Strings.NoLogAvailable,
            Foreground = SystemColors.GrayTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var logGrid = new Grid();
        logGrid.Children.Add(_logBox);
        logGrid.Children.Add(_emptyText);
        panel.Children.Add(logGrid);

        Content = panel;
        RefreshLog();

        // Subscribe to real-time log updates
        _processManager.LogChanged += OnLogChanged;
    }

    private void RefreshLog()
    {
        // Follow new output only if the user hasn't scrolled up to read something
        var atEnd = _logBox.VerticalOffset + _logBox.ViewportHeight >= _logBox.ExtentHeight - 2;
        _logBox.Text = _vm.GetLog();
        if (atEnd) _logBox.ScrollToEnd();
        _emptyText.Visibility = string.IsNullOrEmpty(_logBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ShowCopyResult(bool ok)
    {
        _copyStatus.Text = ok ? Strings.Copied : Strings.CopyFailed;
        _copyStatus.Foreground = ok
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0x7C, 0x10))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C));
        _copyStatus.Visibility = Visibility.Visible;
        await System.Threading.Tasks.Task.Delay(2000);
        _copyStatus.Visibility = Visibility.Collapsed;
    }

    private void OnLogChanged(Guid id)
    {
        if (id != _vm.ConfigId) return;
        Dispatcher.Invoke(RefreshLog);
    }

    protected override void OnClosed(EventArgs e)
    {
        _processManager.LogChanged -= OnLogChanged;
        OpenDialogs.Remove(_vm.ConfigId);
        base.OnClosed(e);
    }
}
