using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using SSHTunnel4Win.Converters;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;
using SSHTunnel4Win.ViewModels;
using SSHTunnel4Win.Views;

namespace SSHTunnel4Win;

public partial class App : Application
{
    private const string MutexName = "SSHTunnel4Win_SingleInstance";
    private static readonly string PipeName = $"SSHTunnel4Win_Activate_{Environment.UserName}";

    private static Mutex? _mutex;
    private TaskbarIcon? _trayIcon;
    private MainViewModel _vm = null!;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private bool _isQuitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = Environment.GetCommandLineArgs();
        var shareUrl = args.Length > 1 && args[1].StartsWith("sshtunnel://") ? args[1] : null;

        // Single instance: hand off to the running copy (show its window, pass along any link)
        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            if (!SignalRunningInstance(shareUrl ?? ""))
                MessageBox.Show(Strings.AlreadyRunning, Strings.SSHTunnelManager,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            _mutex = null;
            Shutdown();
            return;
        }
        StartActivationListener();

        // Initialize services
        var configStore = new ConfigStore();
        var tunnelStatus = new TunnelStatus();
        var processManager = new SSHProcessManager(tunnelStatus);
        var appSettings = new AppSettings();

        _vm = new MainViewModel(configStore, processManager, tunnelStatus, appSettings);

        // Create system tray icon
        CreateTrayIcon();

        // Auto-connect (skip configs that can't possibly connect rather than producing an error)
        foreach (var config in configStore.Configs.Where(c => c.AutoConnect && c.IsConnectable))
            processManager.Connect(config);

        // Open manager on launch
        if (appSettings.OpenManagerOnLaunch)
            ShowMainWindow();

        // Auto-check for updates (silent unless there is one)
        if (appSettings.AutoCheckForUpdates)
            _ = _vm.CheckForUpdatesAsync(userInitiated: false);

        // Handle URL scheme from command line: show it for review instead of importing blindly
        if (shareUrl != null)
            HandleActivation(shareUrl);
    }

    private static bool SignalRunningInstance(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client);
            writer.Write(message);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StartActivationListener()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    var message = reader.ReadToEnd();
                    Dispatcher.BeginInvoke(() => HandleActivation(message));
                }
                catch
                {
                    Thread.Sleep(1000);
                }
            }
        })
        { IsBackground = true, Name = "ActivationListener" };
        thread.Start();
    }

    private void HandleActivation(string message)
    {
        if (_isQuitting) return;
        ShowMainWindow();
        if (message.StartsWith("sshtunnel://"))
            _mainWindow?.ShowImportDialog(message);
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TaskbarIcon { NoLeftClickDelay = true };

        UpdateTrayIcon();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        // Single click opens the manager, as with most Windows tray apps
        _trayIcon.TrayLeftMouseUp += (_, _) => ShowMainWindow();

        // Build context menu
        RebuildTrayMenu();
        _vm.ConfigStore.ConfigsChanged += RebuildTrayMenu;
        _vm.Status.StateChanged += _ => Dispatcher.Invoke(() => { RebuildTrayMenu(); UpdateTrayIcon(); });
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null) return;
        var connectedCount = _vm.ConfigStore.Configs.Count(c => _vm.Status.GetState(c.Id) == ConnectionState.Connected);
        _trayIcon.ToolTipText = connectedCount > 0
            ? string.Format(Strings.TrayTooltipConnected, Strings.SSHTunnelManager, connectedCount)
            : Strings.SSHTunnelManager;

        // Black glyphs vanish on a dark taskbar, so use the white set there
        var suffix = IsTaskbarDark() ? "-white" : "";
        var icoPath = connectedCount > 0 ? $"Assets/tray-icon-connected{suffix}.ico" : $"Assets/tray-icon{suffix}.ico";
        var iconUri = new Uri($"pack://application:,,,/{icoPath}", UriKind.Absolute);
        try
        {
            var iconStream = GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
                _trayIcon.Icon = new System.Drawing.Icon(iconStream);
        }
        catch { }
    }

    /// <summary>
    /// The taskbar follows Windows' "system" theme, which is separate from the apps theme
    /// (dark taskbar with light apps is a common combination).
    /// </summary>
    private static bool IsTaskbarDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // Windows builds without this value always had a dark taskbar
            return key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
        }
        catch
        {
            return false;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Switching light/dark mode raises the General category
        if (e.Category == UserPreferenceCategory.General)
            Dispatcher.BeginInvoke(UpdateTrayIcon);
    }

    private void RebuildTrayMenu()
    {
        var menu = new ContextMenu();

        // Open Manager: the default action, so it's first and bold
        var openItem = new MenuItem { Header = Strings.OpenManager, FontWeight = FontWeights.SemiBold };
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());

        // Tunnel items: status dot + name, with the status spelled out on the right
        var anyActive = false;
        foreach (var config in _vm.ConfigStore.Configs)
        {
            var state = _vm.Status.GetState(config.Id);
            anyActive |= state.IsActive();
            var item = new MenuItem
            {
                Header = config.DisplayName,
                InputGestureText = ConnectionStateToLabelConverter.For(state),
                Icon = new Ellipse { Width = 8, Height = 8, Fill = ConnectionStateToColorConverter.For(state) },
                // Incomplete tunnels can't connect; disable rather than fail with an ssh error
                IsEnabled = state.IsActive() || config.IsConnectable
            };
            var capturedId = config.Id;
            item.Click += (_, _) =>
            {
                if (_vm.ConfigStore.FindById(capturedId) is { } current)
                    _vm.Toggle(current);
            };
            menu.Items.Add(item);
        }

        if (_vm.ConfigStore.Configs.Count > 0)
            menu.Items.Add(new Separator());

        // Reconnect All
        var reconnectAllItem = new MenuItem { Header = Strings.ReconnectAll, IsEnabled = anyActive };
        reconnectAllItem.Click += (_, _) => _vm.ProcessManager.ReconnectAll();
        menu.Items.Add(reconnectAllItem);

        // Disconnect All
        var disconnectItem = new MenuItem { Header = Strings.DisconnectAll, IsEnabled = anyActive };
        disconnectItem.Click += (_, _) => _vm.ProcessManager.DisconnectAll();
        menu.Items.Add(disconnectItem);

        menu.Items.Add(new Separator());

        // Settings
        var settingsItem = new MenuItem { Header = Strings.Settings };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        // Check for Updates
        var updateItem = new MenuItem { Header = Strings.CheckForUpdates };
        updateItem.Click += async (_, _) => await _vm.CheckForUpdatesAsync(userInitiated: true);
        menu.Items.Add(updateItem);

        menu.Items.Add(new Separator());

        // Quit
        var quitItem = new MenuItem { Header = Strings.Quit };
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
            _mainWindow.IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is false) ShowTrayHintOnce();
            };
        }
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    /// <summary>
    /// Closing the window only hides it; tell the user once that the app (and their tunnels) keep running.
    /// </summary>
    private void ShowTrayHintOnce()
    {
        if (_isQuitting || _trayIcon == null || _vm.AppSettings.HasShownTrayHint) return;
        _vm.AppSettings.HasShownTrayHint = true;
        _trayIcon.ShowBalloonTip(Strings.SSHTunnelManager, Strings.StillRunningInTray, BalloonIcon.Info);
    }

    private void ShowSettings()
    {
        // The tray menu stays usable while Settings is open, so don't stack a second copy
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        var settingsVm = new SettingsViewModel(_vm.AppSettings);
        _settingsWindow = new SettingsWindow(settingsVm);
        if (_mainWindow?.IsVisible == true)
            _settingsWindow.Owner = _mainWindow;
        else
            _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _settingsWindow.ShowDialog();
        _settingsWindow = null;
    }

    public void QuitApp()
    {
        _isQuitting = true;
        _vm.ProcessManager.DisconnectOnQuit(_vm.ConfigStore.Configs);
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // SystemEvents is static; unhook so it doesn't keep a reference to a dead app
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
