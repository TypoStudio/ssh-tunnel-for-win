using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;

namespace SSHTunnel4Win.ViewModels;

public enum SidebarTab { Tunnels, SSHConfig }

public partial class MainViewModel : ObservableObject
{
    private readonly ConfigStore _configStore;
    private readonly SSHProcessManager _processManager;
    private readonly TunnelStatus _status;
    private readonly AppSettings _appSettings;
    private readonly SSHConfigStore _sshConfigStore;

    [ObservableProperty] private SidebarTab _selectedTab = SidebarTab.Tunnels;
    [ObservableProperty] private Guid? _selectedTunnelId;
    [ObservableProperty] private Guid? _selectedSshConfigId;
    [ObservableProperty] private TunnelDetailViewModel? _currentTunnelDetail;
    [ObservableProperty] private SSHConfigDetailViewModel? _currentSshConfigDetail;

    public ObservableCollection<SSHTunnelConfig> Tunnels { get; } = new();
    public ObservableCollection<SSHConfigEntry> SshConfigEntries { get; } = new();

    public ConfigStore ConfigStore => _configStore;
    public SSHProcessManager ProcessManager => _processManager;
    public TunnelStatus Status => _status;
    public AppSettings AppSettings => _appSettings;
    public SSHConfigStore SshConfigStore => _sshConfigStore;

    public MainViewModel(ConfigStore configStore, SSHProcessManager processManager, TunnelStatus status, AppSettings appSettings)
    {
        _configStore = configStore;
        _processManager = processManager;
        _status = status;
        _appSettings = appSettings;
        _sshConfigStore = new SSHConfigStore();

        RefreshTunnels();
        RefreshSshConfigs();

        _configStore.ConfigsChanged += RefreshTunnels;
        _sshConfigStore.EntriesChanged += RefreshSshConfigs;
        _status.StateChanged += _ => OnPropertyChanged(nameof(Status));
    }

    partial void OnSelectedTunnelIdChanged(Guid? value)
    {
        CurrentTunnelDetail?.Detach();
        if (value is Guid id)
            CurrentTunnelDetail = new TunnelDetailViewModel(_configStore, _processManager, _status, id);
        else
            CurrentTunnelDetail = null;
    }

    partial void OnSelectedSshConfigIdChanged(Guid? value)
    {
        if (value is Guid id)
            CurrentSshConfigDetail = new SSHConfigDetailViewModel(_sshConfigStore, id);
        else
            CurrentSshConfigDetail = null;
    }

    [RelayCommand]
    private void AddTunnel()
    {
        var config = new SSHTunnelConfig { Name = Strings.NewTunnel };
        _configStore.Add(config);
        SelectedTunnelId = config.Id;
    }

    /// <summary>Deletes a tunnel after the user confirms. Returns true if it was deleted.</summary>
    public bool ConfirmAndDeleteTunnel(Guid id)
    {
        var config = _configStore.FindById(id);
        if (config == null) return false;
        if (!Views.Prompt.Confirm(string.Format(Strings.ConfirmDeleteTunnel, config.DisplayName), Strings.Delete))
            return false;
        _processManager.Disconnect(id);
        _processManager.CancelReconnect(id);
        _configStore.Delete(id);
        if (SelectedTunnelId == id) SelectedTunnelId = null;
        return true;
    }

    /// <summary>
    /// Connects or disconnects a tunnel. Every connect entry point (detail view, list, tray)
    /// goes through here so they all get the same port-conflict prompt.
    /// </summary>
    public void Toggle(SSHTunnelConfig config)
    {
        if (_status.GetState(config.Id).IsActive())
        {
            _processManager.Disconnect(config.Id);
            return;
        }
        if (!config.IsConnectable)
        {
            Views.Prompt.Warn(Strings.ConnectRequirements, config.DisplayName);
            return;
        }
        if (!ConfirmPortConflicts(_processManager, config)) return;
        _processManager.Connect(config);
    }

    public static bool ConfirmPortConflicts(SSHProcessManager processManager, SSHTunnelConfig config)
    {
        var conflicts = processManager.CheckPortConflicts(config);
        if (conflicts.Count == 0) return true;
        return Views.Prompt.Confirm(
            string.Format(Strings.PortConflictMessage, string.Join(", ", conflicts)),
            Strings.PortConflict);
    }

    [RelayCommand]
    private void DisconnectAll() => _processManager.DisconnectAll();

    [RelayCommand]
    private void AddSshConfigEntry()
    {
        var firstFile = _sshConfigStore.ConfigFiles.FirstOrDefault();
        if (firstFile == null)
        {
            // Create ~/.ssh/config if it doesn't exist
            var sshDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            System.IO.Directory.CreateDirectory(sshDir);
            firstFile = System.IO.Path.Combine(sshDir, "config");
            System.IO.File.WriteAllText(firstFile, "");
            _sshConfigStore.Load();
        }
        var entry = new SSHConfigEntry { Host = "new-host", SourceFile = firstFile };
        _sshConfigStore.Add(entry);
        SelectedSshConfigId = entry.Id;
    }

    [RelayCommand]
    private void ReloadSshConfig() => _sshConfigStore.Load();

    public void CopyShareString(SSHTunnelConfig config)
    {
        if (ClipboardHelper.TrySetText(ShareService.Encode(config)))
            Views.Prompt.Info(Strings.Copied);
        else
            Views.Prompt.Warn(Strings.CopyFailed);
    }

    private bool _isCheckingForUpdates;

    [RelayCommand]
    private Task CheckForUpdates() => CheckForUpdatesAsync(userInitiated: true);

    /// <summary>
    /// Checks GitHub for a newer release. Background checks stay silent unless an update exists;
    /// user-initiated checks always report back, including when the check itself failed.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_isCheckingForUpdates) return;
        _isCheckingForUpdates = true;
        UpdateInfo? info;
        try
        {
            info = await UpdateService.CheckForUpdateOrThrowAsync();
        }
        catch
        {
            if (userInitiated) Views.Prompt.Warn(Strings.UpdateCheckFailed, Strings.UpdateFailed);
            return;
        }
        finally
        {
            _isCheckingForUpdates = false;
        }

        if (info == null)
        {
            if (userInitiated) Views.Prompt.Info(Strings.UpToDate);
            return;
        }

        if (!Views.Prompt.Ask(string.Format(Strings.NewVersionAvailable, info.Version), Strings.UpdateAvailable))
            return;

        if (info.InstallerUrl != null)
        {
            try
            {
                await UpdateService.PerformUpdateAsync(info.InstallerUrl, _ => { });
                return;
            }
            catch { }
        }
        Process.Start(new ProcessStartInfo { FileName = info.HtmlUrl, UseShellExecute = true });
    }

    /// <summary>Adds imported tunnels in one save and selects the first so the user lands on it.</summary>
    public void ImportTunnels(System.Collections.Generic.IReadOnlyList<SSHTunnelConfig> configs)
    {
        if (configs.Count == 0) return;
        _configStore.AddRange(configs);
        SelectedTunnelId = configs[0].Id;
    }

    public SSHTunnelConfig? ImportFromShareString(string text)
    {
        var config = ShareService.Decode(text);
        if (config == null) return null;
        _configStore.Add(config);
        SelectedTunnelId = config.Id;
        return config;
    }

    public ConnectionState GetTunnelState(Guid id) => _status.GetState(id);

    private void RefreshTunnels()
    {
        Tunnels.Clear();
        foreach (var c in _configStore.Configs)
            Tunnels.Add(c);
    }

    private void RefreshSshConfigs()
    {
        SshConfigEntries.Clear();
        foreach (var e in _sshConfigStore.Entries)
            SshConfigEntries.Add(e);
    }
}
