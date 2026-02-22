using System.ComponentModel;
using System.Windows;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public partial class MainWindow : Window
{
    private MainViewModel _vm = null!;
    private bool _isTunnelTab = true;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        TunnelList.Initialize(vm);
        SSHConfigList.Initialize(vm);
        vm.PropertyChanged += OnVmPropertyChanged;
        UpdateDetailPane();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentTunnelDetail) or nameof(MainViewModel.CurrentSshConfigDetail))
            UpdateDetailPane();
    }

    private void UpdateDetailPane()
    {
        if (_isTunnelTab)
        {
            if (_vm?.CurrentTunnelDetail != null)
            {
                var view = new TunnelDetailView();
                view.Initialize(_vm.CurrentTunnelDetail, _vm);
                DetailPane.Content = view;
            }
            else
            {
                DetailPane.Content = CreateEmptyState(Strings.NoTunnelSelected, Strings.SelectTunnelHint);
            }
        }
        else
        {
            if (_vm?.CurrentSshConfigDetail != null)
            {
                var view = new SSHConfigDetailView();
                view.Initialize(_vm.CurrentSshConfigDetail);
                DetailPane.Content = view;
            }
            else
            {
                DetailPane.Content = CreateEmptyState(Strings.NoHostSelected, Strings.SelectHostHint);
            }
        }
    }

    private static FrameworkElement CreateEmptyState(string title, string description)
    {
        var panel = new System.Windows.Controls.StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            FontSize = 16,
            Foreground = System.Windows.Media.Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = description,
            Foreground = System.Windows.Media.Brushes.DarkGray,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        return panel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Hide to tray instead of quitting
        e.Cancel = true;
        Hide();
    }

    private void TunnelTabClick(object sender, RoutedEventArgs e)
    {
        _isTunnelTab = true;
        _vm.SelectedTab = SidebarTab.Tunnels;
        UpdateSidebarVisibility();
        UpdateDetailPane();
    }

    private void SSHConfigTabClick(object sender, RoutedEventArgs e)
    {
        _isTunnelTab = false;
        _vm.SelectedTab = SidebarTab.SSHConfig;
        UpdateSidebarVisibility();
        UpdateDetailPane();
    }

    private void UpdateSidebarVisibility()
    {
        TunnelList.Visibility = _isTunnelTab ? Visibility.Visible : Visibility.Collapsed;
        SSHConfigList.Visibility = _isTunnelTab ? Visibility.Collapsed : Visibility.Visible;
        AddBtn.ToolTip = _isTunnelTab ? Strings.AddTunnel : Strings.AddHost;
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportDialog(_vm);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void ProcessListClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ProcessListDialog();
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        if (_isTunnelTab)
            _vm.AddTunnelCommand.Execute(null);
        else
            _vm.AddSshConfigEntryCommand.Execute(null);
    }

    private void HelpClick(object sender, RoutedEventArgs e)
    {
        var dialog = new HelpDialog();
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(new SettingsViewModel(_vm.AppSettings));
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void DisconnectAllClick(object sender, RoutedEventArgs e)
    {
        _vm.DisconnectAllCommand.Execute(null);
    }

    private void CheckForUpdatesClick(object sender, RoutedEventArgs e)
    {
        _vm.CheckForUpdatesCommand.Execute(null);
    }

    private void QuitClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
