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

    [RelayCommand]
    private void DeleteTunnel(Guid id)
    {
        _processManager.Disconnect(id);
        _configStore.Delete(id);
        if (SelectedTunnelId == id) SelectedTunnelId = null;
    }

    [RelayCommand]
    private void ToggleTunnel(SSHTunnelConfig config) => _processManager.Toggle(config);

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

    [RelayCommand]
    private void CopyShareString(SSHTunnelConfig config)
    {
        var encoded = ShareService.Encode(config);
        Clipboard.SetText(encoded);
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        var info = await UpdateService.CheckForUpdateAsync();
        if (info != null)
        {
            var result = MessageBox.Show(
                string.Format(Strings.NewVersionAvailable, info.Version),
                Strings.UpdateAvailable,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                if (info.InstallerUrl != null)
                {
                    try
                    {
                        await UpdateService.PerformUpdateAsync(info.InstallerUrl, _ => { });
                    }
                    catch
                    {
                        Process.Start(new ProcessStartInfo { FileName = info.HtmlUrl, UseShellExecute = true });
                    }
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = info.HtmlUrl, UseShellExecute = true });
                }
            }
        }
        else
        {
            MessageBox.Show(
                Strings.UpToDate,
                Strings.SSHTunnelManager,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    public void ImportFromShareString(string text)
    {
        var config = ShareService.Decode(text);
        if (config == null) return;
        _configStore.Add(config);
        SelectedTunnelId = config.Id;
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
