using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Services;

namespace SSHTunnel4Win.ViewModels;

public partial class SSHConfigDetailViewModel : ObservableObject
{
    private readonly SSHConfigStore _store;
    private readonly Guid _entryId;
    private bool _isLoading;

    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _sourceFile = "";
    [ObservableProperty] private string _hostName = "";
    [ObservableProperty] private string _user = "";
    [ObservableProperty] private string _port = "";
    [ObservableProperty] private string _identityFile = "";
    [ObservableProperty] private string _proxyCommand = "";
    [ObservableProperty] private string _proxyJump = "";
    [ObservableProperty] private string _forwardAgent = "";
    [ObservableProperty] private string _serverAliveInterval = "";
    [ObservableProperty] private string _serverAliveCountMax = "";
    [ObservableProperty] private bool _commented;
    [ObservableProperty] private bool _isTextEditMode;
    [ObservableProperty] private string _textEditContent = "";

    public ObservableCollection<string> AvailableFiles { get; } = new();
    public ObservableCollection<DirectiveViewModel> OtherDirectives { get; } = new();

    public SSHConfigDetailViewModel(SSHConfigStore store, Guid entryId)
    {
        _store = store;
        _entryId = entryId;
        LoadEntry();
    }

    private void LoadEntry()
    {
        var entry = _store.Entries.FirstOrDefault(e => e.Id == _entryId);
        if (entry == null) return;

        _isLoading = true;

        // Load AvailableFiles first so ComboBox has items before SourceFile is set
        AvailableFiles.Clear();
        foreach (var f in _store.ConfigFiles)
            AvailableFiles.Add(f);

        Host = entry.Host;
        SourceFile = entry.SourceFile;
        HostName = entry.GetValue("HostName");
        User = entry.GetValue("User");
        Port = entry.GetValue("Port");
        IdentityFile = entry.GetValue("IdentityFile");
        ProxyCommand = entry.GetValue("ProxyCommand");
        ProxyJump = entry.GetValue("ProxyJump");
        ForwardAgent = entry.GetValue("ForwardAgent");
        ServerAliveInterval = entry.GetValue("ServerAliveInterval");
        ServerAliveCountMax = entry.GetValue("ServerAliveCountMax");
        Commented = entry.Commented;

        // Load other directives
        OtherDirectives.Clear();
        foreach (var d in entry.OtherDirectives)
        {
            var vm = new DirectiveViewModel(d.Key, d.Value);
            vm.PropertyChanged += (_, _) => { if (!_isLoading) SaveDraft(); };
            vm.DeleteRequested += () => { OtherDirectives.Remove(vm); SaveDraft(); };
            OtherDirectives.Add(vm);
        }

        _isLoading = false;
    }

    private void SaveDraft()
    {
        if (_isLoading) return;
        var entry = _store.Entries.FirstOrDefault(e => e.Id == _entryId);
        if (entry == null) return;

        entry.Host = Host;
        entry.SourceFile = SourceFile;
        entry.Commented = Commented;
        entry.SetValue(HostName, "HostName");
        entry.SetValue(User, "User");
        entry.SetValue(Port, "Port");
        entry.SetValue(IdentityFile, "IdentityFile");
        entry.SetValue(ProxyCommand, "ProxyCommand");
        entry.SetValue(ProxyJump, "ProxyJump");
        entry.SetValue(ForwardAgent, "ForwardAgent");
        entry.SetValue(ServerAliveInterval, "ServerAliveInterval");
        entry.SetValue(ServerAliveCountMax, "ServerAliveCountMax");

        // Sync other directives: remove old non-common, add current
        var commonLower = new System.Collections.Generic.HashSet<string>(
            SSHConfigEntry.CommonKeys.Select(k => k.ToLowerInvariant()));
        entry.Directives.RemoveAll(d => !commonLower.Contains(d.Key.ToLowerInvariant()));
        foreach (var vm in OtherDirectives)
        {
            if (!string.IsNullOrWhiteSpace(vm.Key))
                entry.Directives.Add(new SSHConfigDirective { Key = vm.Key, Value = vm.Value });
        }

        _store.Update(entry);
    }

    partial void OnHostChanged(string value) => SaveDraft();
    partial void OnSourceFileChanged(string value) => SaveDraft();
    partial void OnHostNameChanged(string value) => SaveDraft();
    partial void OnUserChanged(string value) => SaveDraft();
    partial void OnPortChanged(string value) => SaveDraft();
    partial void OnIdentityFileChanged(string value) => SaveDraft();
    partial void OnProxyCommandChanged(string value) => SaveDraft();
    partial void OnProxyJumpChanged(string value) => SaveDraft();
    partial void OnForwardAgentChanged(string value) => SaveDraft();
    partial void OnServerAliveIntervalChanged(string value) => SaveDraft();
    partial void OnServerAliveCountMaxChanged(string value) => SaveDraft();
    partial void OnCommentedChanged(bool value) => SaveDraft();

    [RelayCommand]
    private void ToggleTextEdit()
    {
        if (IsTextEditMode)
        {
            // Apply text edits
            var entry = _store.Entries.FirstOrDefault(e => e.Id == _entryId);
            if (entry != null)
            {
                var parsed = SSHConfigParser.ParseFullContent(TextEditContent, entry.SourceFile);
                if (parsed.Count > 0)
                {
                    var newEntry = parsed[0];
                    newEntry.Id = entry.Id;
                    _store.Update(newEntry);
                    LoadEntry();
                }
            }
            IsTextEditMode = false;
        }
        else
        {
            // Build text from current entry
            var entry = _store.Entries.FirstOrDefault(e => e.Id == _entryId);
            if (entry == null) return;
            TextEditContent = SSHConfigParser.Serialize(new System.Collections.Generic.List<SSHConfigEntry> { entry });
            IsTextEditMode = true;
        }
    }

    [RelayCommand]
    private void OpenInEditor()
    {
        var file = SourceFile;
        if (string.IsNullOrEmpty(file)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = file, UseShellExecute = true });
        }
        catch { }
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
    private void AddDirective()
    {
        var vm = new DirectiveViewModel("", "");
        vm.PropertyChanged += (_, _) => { if (!_isLoading) SaveDraft(); };
        vm.DeleteRequested += () => { OtherDirectives.Remove(vm); SaveDraft(); };
        OtherDirectives.Add(vm);
    }
}

public partial class DirectiveViewModel : ObservableObject
{
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _value = "";

    public event Action? DeleteRequested;

    public DirectiveViewModel(string key, string value)
    {
        _key = key;
        _value = value;
    }

    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke();
}
