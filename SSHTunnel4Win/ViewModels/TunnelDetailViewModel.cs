using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;

namespace SSHTunnel4Win.ViewModels;

public partial class TunnelDetailViewModel : ObservableObject
{
    private readonly ConfigStore _store;
    private readonly SSHProcessManager _processManager;
    private readonly TunnelStatus _status;
    private readonly Guid _configId;
    private bool _isLoading;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _portText = "22";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private AuthMethod _authMethod = AuthMethod.IdentityFile;
    [ObservableProperty] private string _identityFile = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _autoConnect;
    [ObservableProperty] private bool _disconnectOnQuit = true;
    [ObservableProperty] private string _additionalArgs = "";
    [ObservableProperty] private ConnectionState _state = ConnectionState.Disconnected;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _showCopied;

    public ObservableCollection<TunnelEntryViewModel> TunnelEntries { get; } = new();

    public Guid ConfigId => _configId;

    public bool CanConnect => !string.IsNullOrEmpty(Host) && !string.IsNullOrEmpty(Username) && TunnelEntries.Count > 0;

    public TunnelDetailViewModel(ConfigStore store, SSHProcessManager processManager, TunnelStatus status, Guid configId)
    {
        _store = store;
        _processManager = processManager;
        _status = status;
        _configId = configId;
        _status.StateChanged += OnStatusChanged;
        LoadConfig();
    }

    private void OnStatusChanged(Guid id)
    {
        if (id != _configId) return;
        State = _status.GetState(id);
        ErrorMessage = _status.GetErrorMessage(id);
    }

    private void LoadConfig()
    {
        var config = _store.FindById(_configId);
        if (config == null) return;

        _isLoading = true;
        Name = config.Name;
        Host = config.Host;
        PortText = config.Port.ToString();
        Username = config.Username;
        AuthMethod = config.AuthMethod;
        IdentityFile = config.IdentityFile;
        Password = CredentialService.GetPassword(config.Id) ?? "";
        AutoConnect = config.AutoConnect;
        DisconnectOnQuit = config.DisconnectOnQuit;
        AdditionalArgs = config.AdditionalArgs;
        State = _status.GetState(config.Id);
        ErrorMessage = _status.GetErrorMessage(config.Id);

        TunnelEntries.Clear();
        foreach (var entry in config.Tunnels)
        {
            var vm = new TunnelEntryViewModel(entry);
            vm.PropertyChanged += (_, _) => { if (!_isLoading) SaveDraft(); };
            vm.DeleteRequested += () => { TunnelEntries.Remove(vm); SaveDraft(); };
            TunnelEntries.Add(vm);
        }

        _isLoading = false;
        OnPropertyChanged(nameof(CanConnect));
    }

    private void SaveDraft()
    {
        if (_isLoading) return;
        var config = BuildConfig();
        _store.Update(config);
        OnPropertyChanged(nameof(CanConnect));
    }

    private SSHTunnelConfig BuildConfig() => new()
    {
        Id = _configId,
        Name = Name,
        Host = Host,
        Port = ushort.TryParse(PortText, out var p) ? p : (ushort)22,
        Username = Username,
        AuthMethod = AuthMethod,
        IdentityFile = IdentityFile,
        Tunnels = TunnelEntries.Select(e => e.ToModel()).ToList(),
        AutoConnect = AutoConnect,
        DisconnectOnQuit = DisconnectOnQuit,
        AdditionalArgs = AdditionalArgs
    };

    partial void OnNameChanged(string value) => SaveDraft();
    partial void OnHostChanged(string value) => SaveDraft();
    partial void OnPortTextChanged(string value) => SaveDraft();
    partial void OnUsernameChanged(string value) => SaveDraft();
    partial void OnAuthMethodChanged(AuthMethod value) => SaveDraft();
    partial void OnIdentityFileChanged(string value) => SaveDraft();
    partial void OnAutoConnectChanged(bool value) => SaveDraft();
    partial void OnDisconnectOnQuitChanged(bool value) => SaveDraft();
    partial void OnAdditionalArgsChanged(string value) => SaveDraft();

    partial void OnPasswordChanged(string value)
    {
        if (_isLoading) return;
        if (string.IsNullOrEmpty(value))
            CredentialService.DeletePassword(_configId);
        else
            CredentialService.SavePassword(value, _configId);
    }

    [RelayCommand]
    private void ToggleConnect()
    {
        if (State.IsActive())
        {
            _processManager.Disconnect(_configId);
            return;
        }
        var config = BuildConfig();
        var conflicts = _processManager.CheckPortConflicts(config);
        if (conflicts.Count > 0)
        {
            var ports = string.Join(", ", conflicts);
            var result = MessageBox.Show(
                string.Format(Strings.PortConflictMessage, ports),
                Strings.PortConflict,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }
        _processManager.Connect(config);
    }

    [RelayCommand]
    private void AddTunnelEntry()
    {
        var entry = new TunnelEntry();
        var vm = new TunnelEntryViewModel(entry);
        vm.PropertyChanged += (_, _) => { if (!_isLoading) SaveDraft(); };
        vm.DeleteRequested += () => { TunnelEntries.Remove(vm); SaveDraft(); };
        TunnelEntries.Add(vm);
        SaveDraft();
    }

    [RelayCommand]
    private void BrowseIdentityFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            InitialDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"),
            Filter = "All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            IdentityFile = dialog.FileName;
    }

    [RelayCommand]
    private void CopyShareString()
    {
        var config = BuildConfig();
        var encoded = ShareService.Encode(config);
        Clipboard.SetText(encoded);
        ShowCopied = true;
        System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
            Application.Current?.Dispatcher.Invoke(() => ShowCopied = false));
    }

    [RelayCommand]
    private void CopyCLICommand()
    {
        var config = BuildConfig();
        var cli = ShareService.BuildCLI(config);
        Clipboard.SetText(cli);
        ShowCopied = true;
        System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
            Application.Current?.Dispatcher.Invoke(() => ShowCopied = false));
    }

    public string GetLog() => _processManager.Logs.TryGetValue(_configId, out var log) ? log : "";

    public void ApplySSHConfig(SSHConfigHost host)
    {
        _isLoading = true;
        if (string.IsNullOrEmpty(Name) || Name == Strings.NewTunnel)
            Name = host.Name;
        Host = host.Hostname;
        PortText = host.Port.ToString();
        if (!string.IsNullOrEmpty(host.User)) Username = host.User;
        if (!string.IsNullOrEmpty(host.IdentityFile))
        {
            AuthMethod = AuthMethod.IdentityFile;
            IdentityFile = host.IdentityFile;
        }
        _isLoading = false;
        SaveDraft();
    }
}

public partial class TunnelEntryViewModel : ObservableObject
{
    private readonly Guid _id;

    [ObservableProperty] private TunnelType _type = TunnelType.Local;
    [ObservableProperty] private string _localPortText = "0";
    [ObservableProperty] private string _remoteHost = "localhost";
    [ObservableProperty] private string _remotePortText = "0";
    [ObservableProperty] private string _bindAddress = "";

    public event Action? DeleteRequested;

    public TunnelEntryViewModel(TunnelEntry entry)
    {
        _id = entry.Id;
        _type = entry.Type;
        _localPortText = entry.LocalPort.ToString();
        _remoteHost = entry.RemoteHost;
        _remotePortText = entry.RemotePort.ToString();
        _bindAddress = entry.BindAddress;
    }

    public TunnelEntry ToModel() => new()
    {
        Id = _id,
        Type = Type,
        LocalPort = ushort.TryParse(LocalPortText, out var lp) ? lp : (ushort)0,
        RemoteHost = RemoteHost,
        RemotePort = ushort.TryParse(RemotePortText, out var rp) ? rp : (ushort)0,
        BindAddress = BindAddress
    };

    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke();
}
