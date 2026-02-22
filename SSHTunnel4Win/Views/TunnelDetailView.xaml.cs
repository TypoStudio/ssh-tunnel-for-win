using System.Windows;
using System.Windows.Controls;
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
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TunnelDetailViewModel.AuthMethod))
                UpdateAuthVisibility();
            if (e.PropertyName is nameof(TunnelDetailViewModel.State) or nameof(TunnelDetailViewModel.ErrorMessage))
            {
                UpdateConnectLabel();
                ErrorText.Text = _vm.ErrorMessage;
                ErrorText.Visibility = string.IsNullOrEmpty(_vm.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
            }
        };

        // Set initial password
        if (!string.IsNullOrEmpty(vm.Password))
            PasswordBox.Password = vm.Password;
    }

    private void UpdateAuthVisibility()
    {
        IdentityFilePanel.Visibility = _vm.AuthMethod == AuthMethod.IdentityFile ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox.Visibility = _vm.AuthMethod == AuthMethod.Password ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateConnectLabel()
    {
        ConnectLabel.Text = _vm.State.IsActive()
            ? Strings.Disconnect
            : Strings.Connect;
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
            MessageBox.Show(Strings.NoHostsInConfig, Strings.LoadFromSSHConfig,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new SSHConfigPickerDialog(hosts);
        picker.Owner = Window.GetWindow(this);
        if (picker.ShowDialog() == true && picker.SelectedHost != null)
            _vm.ApplySSHConfig(picker.SelectedHost);
    }

    private void ShowLog(object sender, RoutedEventArgs e)
    {
        var dialog = new LogDialog(_vm, _mainVm.ProcessManager);
        dialog.Owner = Window.GetWindow(this);
        dialog.Show();
    }
}

// SSH Config Picker Dialog
public partial class SSHConfigPickerDialog : Window
{
    public SSHConfigHost? SelectedHost { get; private set; }
    private readonly System.Collections.Generic.List<SSHConfigHost> _allHosts;

    public SSHConfigPickerDialog(System.Collections.Generic.List<SSHConfigHost> hosts)
    {
        _allHosts = hosts;
        Title = Strings.LoadFromSSHConfig;
        Width = 400;
        Height = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(16) };

        var searchBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(searchBox);

        var listBox = new ListBox { Height = 230 };
        listBox.DisplayMemberPath = "DisplayText";
        panel.Children.Add(listBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancelBtn = new Button { Content = Strings.Cancel, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        RefreshList(listBox, "");

        searchBox.TextChanged += (_, _) => RefreshList(listBox, searchBox.Text);
        listBox.MouseDoubleClick += (_, _) =>
        {
            if (listBox.SelectedItem is HostItem item)
            {
                SelectedHost = item.Host;
                DialogResult = true;
                Close();
            }
        };

        Content = panel;
    }

    private void RefreshList(ListBox listBox, string query)
    {
        var lower = query.ToLowerInvariant();
        var filtered = string.IsNullOrEmpty(lower)
            ? _allHosts
            : _allHosts.FindAll(h =>
                h.Name.ToLowerInvariant().Contains(lower) ||
                h.Hostname.ToLowerInvariant().Contains(lower) ||
                h.User.ToLowerInvariant().Contains(lower));
        listBox.ItemsSource = filtered.ConvertAll(h => new HostItem(h));
    }

    private class HostItem
    {
        public SSHConfigHost Host { get; }
        public string DisplayText => $"{Host.Name}  ({(string.IsNullOrEmpty(Host.User) ? "" : $"{Host.User}@")}{Host.Hostname}{(Host.Port != 22 ? $":{Host.Port}" : "")})";
        public HostItem(SSHConfigHost host) => Host = host;
    }
}

// Log Dialog (non-modal, real-time)
public class LogDialog : Window
{
    private readonly TunnelDetailViewModel _vm;
    private readonly SSHTunnel4Win.Services.SSHProcessManager _processManager;
    private readonly TextBox _logBox;

    public LogDialog(TunnelDetailViewModel vm, SSHTunnel4Win.Services.SSHProcessManager processManager)
    {
        _vm = vm;
        _processManager = processManager;
        Title = Strings.ConnectionLog + " - " + (string.IsNullOrEmpty(vm.Name) ? vm.Host : vm.Name);
        Width = 600;
        Height = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new DockPanel { Margin = new Thickness(16) };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(toolbar, Dock.Top);
        var copyBtn = new Button { Content = Strings.CopyLog, Margin = new Thickness(0, 0, 8, 0) };
        copyBtn.Click += (_, _) => Clipboard.SetText(_logBox.Text);
        toolbar.Children.Add(copyBtn);
        var clearBtn = new Button { Content = Strings.ClearLog };
        clearBtn.Click += (_, _) => _logBox.Text = "";
        toolbar.Children.Add(clearBtn);
        panel.Children.Add(toolbar);

        var closeBtn = new Button { Content = Strings.Close, HorizontalAlignment = HorizontalAlignment.Right, Width = 80, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(closeBtn, Dock.Bottom);
        closeBtn.Click += (_, _) => Close();
        panel.Children.Add(closeBtn);

        _logBox = new TextBox
        {
            Text = vm.GetLog(),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11
        };
        panel.Children.Add(_logBox);

        Content = panel;

        // Subscribe to real-time log updates
        _processManager.LogChanged += OnLogChanged;
    }

    private void OnLogChanged(System.Guid id)
    {
        if (id != _vm.ConfigId) return;
        Dispatcher.Invoke(() =>
        {
            _logBox.Text = _vm.GetLog();
            _logBox.ScrollToEnd();
        });
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _processManager.LogChanged -= OnLogChanged;
        base.OnClosed(e);
    }
}
