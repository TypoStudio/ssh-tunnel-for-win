using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public class TunnelListItem : INotifyPropertyChanged
{
    public SSHTunnelConfig Config { get; }
    private readonly TunnelStatus _status;

    public string DisplayName => string.IsNullOrEmpty(Config.Name) ? Config.Host : Config.Name;
    public string Subtitle => $"{Config.Username}@{Config.Host}:{Config.Port}";
    public int TunnelCount => Config.Tunnels.Count;
    public bool HasTunnels => Config.Tunnels.Count > 0;
    public ConnectionState State => _status.GetState(Config.Id);
    public string ConnectLabel => State.IsActive() ? Strings.Disconnect : Strings.Connect;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TunnelListItem(SSHTunnelConfig config, TunnelStatus status)
    {
        Config = config;
        _status = status;
    }

    public void RaiseStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConnectLabel)));
    }
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
        RefreshList();
    }

    private void RefreshList()
    {
        _items.Clear();
        foreach (var config in _vm.ConfigStore.Configs)
            _items.Add(new TunnelListItem(config, _vm.Status));
        TunnelListBox.ItemsSource = null;
        TunnelListBox.ItemsSource = _items;
        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void ToggleConnect(object sender, RoutedEventArgs e)
    {
        if (GetContextConfig(sender) is { } config)
            _vm.ProcessManager.Toggle(config);
    }

    private void CopyShareString(object sender, RoutedEventArgs e)
    {
        if (GetContextConfig(sender) is { } config)
        {
            var encoded = ShareService.Encode(config);
            Clipboard.SetText(encoded);
        }
    }

    private void DeleteTunnel(object sender, RoutedEventArgs e)
    {
        if (GetContextConfig(sender) is { } config)
            _vm.DeleteTunnelCommand.Execute(config.Id);
    }

    private SSHTunnelConfig? GetContextConfig(object sender)
    {
        if (sender is MenuItem { DataContext: TunnelListItem item })
            return item.Config;
        return null;
    }
}
