using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SSHTunnel4Win.Converters;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public class TunnelListItem : INotifyPropertyChanged
{
    public SSHTunnelConfig Config { get; private set; }
    private readonly TunnelStatus _status;

    public string DisplayName => Config.DisplayName;

    public string Subtitle
    {
        get
        {
            // Avoid showing a bare "@:22" for a tunnel that hasn't been filled in yet
            if (string.IsNullOrEmpty(Config.Host)) return "";
            var user = string.IsNullOrEmpty(Config.Username) ? "" : Config.Username + "@";
            var port = Config.Port == 22 ? "" : $":{Config.Port}";
            return user + Config.Host + port;
        }
    }

    public int TunnelCount => Config.Tunnels.Count;
    public bool HasTunnels => Config.Tunnels.Count > 0;
    public ConnectionState State => _status.GetState(Config.Id);
    public string StateLabel => ConnectionStateToLabelConverter.For(State);
    public string ConnectLabel => State.IsActive() ? Strings.Disconnect : Strings.Connect;
    public bool IsActive => State.IsActive();
    public bool CanToggle => IsActive || Config.IsConnectable;
    public string AccessibleName => $"{DisplayName}, {StateLabel}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public TunnelListItem(SSHTunnelConfig config, TunnelStatus status)
    {
        Config = config;
        _status = status;
    }

    public void Update(SSHTunnelConfig config)
    {
        Config = config;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public void RaiseStateChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public partial class TunnelListView : UserControl
{
    private MainViewModel _vm = null!;
    private readonly List<TunnelListItem> _items = new();

    public TunnelListView()
    {
        InitializeComponent();
    }

    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        _vm.ConfigStore.ConfigsChanged += RefreshList;
        _vm.Status.StateChanged += OnStateChanged;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTunnelId))
                SyncSelection();
        };
        RefreshList();
    }

    private void RefreshList()
    {
        var configs = _vm.ConfigStore.Configs;
        // Edits in the detail pane save on every keystroke; update rows in place so the
        // selection highlight and scroll position survive instead of rebuilding the list.
        if (configs.Count == _items.Count && configs.Select(c => c.Id).SequenceEqual(_items.Select(i => i.Config.Id)))
        {
            for (int i = 0; i < configs.Count; i++)
                _items[i].Update(configs[i]);
        }
        else
        {
            _items.Clear();
            foreach (var config in configs)
                _items.Add(new TunnelListItem(config, _vm.Status));
            TunnelListBox.ItemsSource = null;
            TunnelListBox.ItemsSource = _items;
            SyncSelection();
        }
        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Offer the PuTTY import on first run, when it's most useful
        if (_items.Count == 0)
            ImportPuttyButton.Visibility = PuttySessionImporter.AvailableSources().Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ImportPuttyClick(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.ShowPuttyImportDialog();

    private void SyncSelection()
    {
        var item = _items.FirstOrDefault(i => i.Config.Id == _vm.SelectedTunnelId);
        if (TunnelListBox.SelectedItem != item)
            TunnelListBox.SelectedItem = item;
        if (item != null)
            TunnelListBox.ScrollIntoView(item);
    }

    private void OnStateChanged(Guid id)
    {
        Dispatcher.Invoke(() =>
        {
            var item = _items.FirstOrDefault(i => i.Config.Id == id);
            item?.RaiseStateChanged();
        });
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TunnelListBox.SelectedItem is TunnelListItem item)
            _vm.SelectedTunnelId = item.Config.Id;
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && TunnelListBox.SelectedItem is TunnelListItem item)
        {
            _vm.ConfirmAndDeleteTunnel(item.Config.Id);
            e.Handled = true;
        }
    }

    private void ToggleConnect(object sender, RoutedEventArgs e)
    {
        if (GetContextConfig(sender) is { } config)
            _vm.Toggle(config);
    }

    private void ReconnectTunnel(object sender, RoutedEventArgs e)
    {
        if (GetContextConfig(sender) is { } config)
            _vm.ProcessManager.Reconnect(config);
    }

    private void CopyShareString(object sender, RoutedEventArgs e)
    {
        if (GetContextConfig(sender) is { } config)
            _vm.CopyShareString(config);
    }

    private void DeleteTunnel(object sender, RoutedEventArgs e)
    {
        if (GetContextConfig(sender) is { } config)
            _vm.ConfirmAndDeleteTunnel(config.Id);
    }

    private static SSHTunnelConfig? GetContextConfig(object sender)
    {
        if (sender is MenuItem { DataContext: TunnelListItem item })
            return item.Config;
        return null;
    }
}
