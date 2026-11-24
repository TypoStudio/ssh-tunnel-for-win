using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand AddCommand = new("Add", nameof(AddCommand), typeof(MainWindow));
    public static readonly RoutedUICommand ImportCommand = new("Import", nameof(ImportCommand), typeof(MainWindow));
    public static readonly RoutedUICommand PasteImportCommand = new("PasteImport", nameof(PasteImportCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SettingsCommand = new("Settings", nameof(SettingsCommand), typeof(MainWindow));
    public static readonly RoutedUICommand QuitCommand = new("Quit", nameof(QuitCommand), typeof(MainWindow));
    public static readonly RoutedUICommand HelpCommand = new("Help", nameof(HelpCommand), typeof(MainWindow));

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

    internal static FrameworkElement CreateEmptyState(string title, string description)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16)
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            Foreground = SystemColors.GrayTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = SystemColors.GrayTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260
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
        var addLabel = _isTunnelTab ? Strings.AddTunnel : Strings.AddHost;
        AddBtn.ToolTip = addLabel;
        AutomationProperties.SetName(AddBtn, addLabel);
        AddMenuItem.Header = addLabel;
    }

    private void ImportClick(object sender, RoutedEventArgs e) => ShowImportDialog(null);

    private void PasteImport(object sender, RoutedEventArgs e)
    {
        var text = ClipboardHelper.TryGetText();
        if (!string.IsNullOrWhiteSpace(text))
            ShowImportDialog(text);
    }

    public void ShowImportDialog(string? initialText)
    {
        // Imports always land in the tunnel list, so make sure it's the visible tab
        if (!_isTunnelTab)
        {
            TunnelTabBtn.IsChecked = true;
            TunnelTabClick(this, new RoutedEventArgs());
        }
        var dialog = new ImportDialog(_vm, initialText) { Owner = this };
        dialog.ShowDialog();
    }

    private void ImportPuttyClick(object sender, RoutedEventArgs e) => ShowPuttyImportDialog();

    public void ShowPuttyImportDialog()
    {
        // Imports land in the tunnel list, so make sure it's the visible tab
        if (!_isTunnelTab)
        {
            TunnelTabBtn.IsChecked = true;
            TunnelTabClick(this, new RoutedEventArgs());
        }
        var dialog = new PuttyImportDialog(_vm) { Owner = this };
        dialog.ShowDialog();
    }

    private void ProcessListClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ProcessListDialog { Owner = this };
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
        var dialog = new HelpDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(new SettingsViewModel(_vm.AppSettings)) { Owner = this };
        dialog.ShowDialog();
    }

    private void ReconnectAllClick(object sender, RoutedEventArgs e) => _vm.ProcessManager.ReconnectAll();

    private void DisconnectAllClick(object sender, RoutedEventArgs e) => _vm.DisconnectAllCommand.Execute(null);

    private void CheckForUpdatesClick(object sender, RoutedEventArgs e) => _vm.CheckForUpdatesCommand.Execute(null);

    private void QuitClick(object sender, RoutedEventArgs e) => ((App)Application.Current).QuitApp();
}
