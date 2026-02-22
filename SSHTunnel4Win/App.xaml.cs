using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;
using SSHTunnel4Win.ViewModels;
using SSHTunnel4Win.Views;

namespace SSHTunnel4Win;

public partial class App : Application
{
    private static Mutex? _mutex;
    private TaskbarIcon? _trayIcon;
    private MainViewModel _vm = null!;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        _mutex = new Mutex(true, "SSHTunnel4Win_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("SSH Tunnel Manager is already running.",
                "SSH Tunnel Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Initialize services
        var configStore = new ConfigStore();
        var tunnelStatus = new TunnelStatus();
        var processManager = new SSHProcessManager(tunnelStatus);
        var appSettings = new AppSettings();

        _vm = new MainViewModel(configStore, processManager, tunnelStatus, appSettings);

        // Create system tray icon
        CreateTrayIcon();

        // Auto-connect
        foreach (var config in configStore.Configs.Where(c => c.AutoConnect))
            processManager.Connect(config);

        // Open manager on launch
        if (appSettings.OpenManagerOnLaunch)
            ShowMainWindow();

        // Auto-check for updates
        if (appSettings.AutoCheckForUpdates)
        {
            Task.Run(async () =>
            {
                var info = await UpdateService.CheckForUpdateAsync();
                if (info != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var result = MessageBox.Show(
                            string.Format(Strings.NewVersionAvailable, info.Version),
                            Strings.UpdateAvailable,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);
                        if (result == MessageBoxResult.Yes && info.InstallerUrl != null)
                        {
                            Process.Start(new ProcessStartInfo { FileName = info.HtmlUrl, UseShellExecute = true });
                        }
                    });
                }
            });
        }

        // Handle URL scheme from command line
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && args[1].StartsWith("sshtunnel://"))
        {
            _vm.ImportFromShareString(args[1]);
            ShowMainWindow();
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "SSH Tunnel Manager"
        };

        // Set icon from resource
        var iconUri = new Uri("pack://application:,,,/Assets/tray-icon.ico", UriKind.Absolute);
        try
        {
            var iconStream = GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
                _trayIcon.Icon = new System.Drawing.Icon(iconStream);
        }
        catch
        {
            // Fallback: will show no icon, but app still works
        }

        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        // Build context menu
        RebuildTrayMenu();
        _vm.ConfigStore.ConfigsChanged += RebuildTrayMenu;
        _vm.Status.StateChanged += _ => Dispatcher.Invoke(RebuildTrayMenu);
    }

    private void RebuildTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        // Tunnel items
        foreach (var config in _vm.ConfigStore.Configs)
        {
            var state = _vm.Status.GetState(config.Id);
            var header = $"{(state.IsActive() ? "\u25CF" : "\u25CB")} {(string.IsNullOrEmpty(config.Name) ? config.Host : config.Name)}";
            var item = new System.Windows.Controls.MenuItem { Header = header };
            var capturedConfig = config;
            item.Click += (_, _) => _vm.ProcessManager.Toggle(capturedConfig);
            menu.Items.Add(item);
        }

        if (_vm.ConfigStore.Configs.Count > 0)
            menu.Items.Add(new System.Windows.Controls.Separator());

        // Open Manager
        var openItem = new System.Windows.Controls.MenuItem { Header = Strings.OpenManager };
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Disconnect All
        var disconnectItem = new System.Windows.Controls.MenuItem { Header = Strings.DisconnectAll };
        disconnectItem.Click += (_, _) => _vm.ProcessManager.DisconnectAll();
        menu.Items.Add(disconnectItem);

        // Check for Updates
        var updateItem = new System.Windows.Controls.MenuItem { Header = Strings.CheckForUpdates };
        updateItem.Click += async (_, _) => await _vm.CheckForUpdatesCommand.ExecuteAsync(null);
        menu.Items.Add(updateItem);

        // Settings
        var settingsItem = new System.Windows.Controls.MenuItem { Header = Strings.Settings };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Quit
        var quitItem = new System.Windows.Controls.MenuItem { Header = Strings.Quit };
        quitItem.Click += (_, _) => QuitApp();
        menu.Items.Add(quitItem);

        if (_trayIcon != null)
            _trayIcon.ContextMenu = menu;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Initialize(_vm);
        }
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ShowSettings()
    {
        var settingsVm = new SettingsViewModel(_vm.AppSettings);
        var window = new SettingsWindow(settingsVm);
        if (_mainWindow?.IsLoaded == true)
            window.Owner = _mainWindow;
        window.ShowDialog();
    }

    private void QuitApp()
    {
        _vm.ProcessManager.DisconnectOnQuit(_vm.ConfigStore.Configs);
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
