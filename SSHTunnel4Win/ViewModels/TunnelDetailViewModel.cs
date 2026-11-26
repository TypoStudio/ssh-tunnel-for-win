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
    [ObservableProperty] private bool _autoReconnect = true;
    [ObservableProperty] private string _additionalArgs = "";
    [ObservableProperty] private ConnectionState _state = ConnectionState.Disconnected;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _showCopied;
    [ObservableProperty] private string _copyFeedback = "";
    [ObservableProperty] private bool _copyFailed;
    private int _copyFeedbackToken;

    public ObservableCollection<TunnelEntryViewModel> TunnelEntries { get; } = new();

    public Guid ConfigId => _configId;

    public bool CanConnect => BuildConfig().IsConnectable;

    /// <summary>Disconnect is always allowed; connect only once the config is complete.</summary>
    public bool CanToggleConnect => IsActive || CanConnect;

    /// <summary>Explains why Connect is disabled; null when it isn't.</summary>
    public string? ConnectDisabledReason => CanToggleConnect ? null : Strings.ConnectRequirements;

    public TunnelDetailViewModel(ConfigStore store, SSHProcessManager processManager, TunnelStatus status, Guid configId)
    {
        _store = store;
        _processManager = processManager;
        _status = status;
        _configId = configId;
        _status.StateChanged += OnStatusChanged;
        LoadConfig();
    }

    public bool IsActive => State.IsActive();

    private void OnStatusChanged(Guid id)
    {
        if (id != _configId) return;
        State = _status.GetState(id);
        ErrorMessage = _status.GetErrorMessage(id);
        OnPropertyChanged(nameof(IsActive));
        RefreshConnectability();
    }

    /// <summary>Stops listening for status changes once this view model is no longer shown.</summary>
    public void Detach() => _status.StateChanged -= OnStatusChanged;

    private void RefreshConnectability()
    {
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanToggleConnect));
        OnPropertyChanged(nameof(ConnectDisabledReason));
        ToggleConnectCommand.NotifyCanExecuteChanged();
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
        AutoReconnect = config.AutoReconnect;
        AdditionalArgs = config.AdditionalArgs;
        State = _status.GetState(config.Id);
        ErrorMessage = _status.GetErrorMessage(config.Id);

        TunnelEntries.Clear();
        foreach (var entry in config.Tunnels)
            AddEntry(entry);

        _isLoading = false;
        RefreshConnectability();
    }

    private void SaveDraft()
    {
        if (_isLoading) return;
        var config = BuildConfig();
        _store.Update(config);
        RefreshConnectability();
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
        AutoReconnect = AutoReconnect,
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
    partial void OnAutoReconnectChanged(bool value) => SaveDraft();
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
    private void Reconnect()
    {
        var config = BuildConfig();
        _processManager.Reconnect(config);
    }

    [RelayCommand(CanExecute = nameof(CanToggleConnect))]
    private void ToggleConnect()
    {
        if (State.IsActive())
        {
            _processManager.Disconnect(_configId);
            return;
        }
        var config = BuildConfig();
        if (!MainViewModel.ConfirmPortConflicts(_processManager, config)) return;
        _processManager.Connect(config);
    }

    private void AddEntry(TunnelEntry entry)
    {
        var vm = new TunnelEntryViewModel(entry);
        vm.PropertyChanged += (_, _) => { if (!_isLoading) SaveDraft(); };
        vm.DeleteRequested += () => { TunnelEntries.Remove(vm); SaveDraft(); };
        TunnelEntries.Add(vm);
    }

    [RelayCommand]
    private void AddTunnelEntry()
    {
        AddEntry(new TunnelEntry());
        SaveDraft();
    }

    [RelayCommand]
    private void AddFromCLI()
    {
        var dialog = new Views.ForwardingImportDialog { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true) return;

        foreach (var entry in dialog.Entries)
            AddEntry(entry);
        SaveDraft();
    }

    [RelayCommand]
    private void BrowseIdentityFile()
    {
        var currentDir = string.IsNullOrEmpty(IdentityFile) ? null : System.IO.Path.GetDirectoryName(IdentityFile);
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            InitialDirectory = currentDir != null && System.IO.Directory.Exists(currentDir)
                ? currentDir
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"),
            Filter = $"{Strings.PrivateKeys} (*.ppk;id_*;*.pem;*.key)|*.ppk;id_*;*.pem;*.key|{Strings.AllFiles} (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            IdentityFile = dialog.FileName;
    }

    [RelayCommand]
    private void CopyShareString() => CopyWithFeedback(ShareService.Encode(BuildConfig()));

    [RelayCommand]
    private void CopyCLICommand() => CopyWithFeedback(ShareService.BuildCLI(BuildConfig()));

    private async void CopyWithFeedback(string text)
    {
        var ok = ClipboardHelper.TrySetText(text);
        CopyFailed = !ok;
        CopyFeedback = ok ? Strings.Copied : Strings.CopyFailed;
        ShowCopied = true;
        // Only the most recent copy may hide the message
        var token = ++_copyFeedbackToken;
        await System.Threading.Tasks.Task.Delay(2000);
        if (token == _copyFeedbackToken) ShowCopied = false;
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
    [ObservableProperty] private string _localPortText = "";
    [ObservableProperty] private string _remoteHost = "localhost";
    [ObservableProperty] private string _remotePortText = "";
    [ObservableProperty] private string _bindAddress = "";

    public event Action? DeleteRequested;

    public TunnelEntryViewModel(TunnelEntry entry)
    {
        _id = entry.Id;
        _type = entry.Type;
        // A blank field reads as "fill me in"; a literal 0 reads as a real value
        _localPortText = entry.LocalPort == 0 ? "" : entry.LocalPort.ToString();
        _remoteHost = entry.RemoteHost;
        _remotePortText = entry.RemotePort == 0 ? "" : entry.RemotePort.ToString();
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
