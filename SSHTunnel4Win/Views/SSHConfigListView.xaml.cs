using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public class SSHConfigListItem
{
    public SSHConfigEntry Entry { get; }
    public string Host => (Entry.Commented ? "# " : "") + Entry.Host;
    public string FileName => Path.GetFileName(Entry.SourceFile);

    public SSHConfigListItem(SSHConfigEntry entry) => Entry = entry;
}

public partial class SSHConfigListView : UserControl
{
    private MainViewModel _vm = null!;
    private List<SSHConfigListItem> _allItems = new();

    public SSHConfigListView()
    {
        InitializeComponent();
    }

    public void Initialize(MainViewModel vm)
    {
        _vm = vm;
        _vm.SshConfigStore.EntriesChanged += RefreshList;
        RefreshList();
    }

    private void RefreshList()
    {
        _allItems = _vm.SshConfigStore.Entries.Select(e => new SSHConfigListItem(e)).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text?.ToLowerInvariant() ?? "";
        var filtered = string.IsNullOrEmpty(query)
            ? _allItems
            : _allItems.Where(i =>
                i.Entry.Host.ToLowerInvariant().Contains(query) ||
                i.Entry.GetValue("HostName").ToLowerInvariant().Contains(query))
                .ToList();
        ConfigListBox.ItemsSource = filtered;
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConfigListBox.SelectedItem is SSHConfigListItem item)
            _vm.SelectedSshConfigId = item.Entry.Id;
    }

    private void ToggleComment(object sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
            _vm.SshConfigStore.ToggleComment(entry.Id);
    }

    private void MoveUp(object sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
            _vm.SshConfigStore.MoveEntry(entry.Id, -1);
    }

    private void MoveDown(object sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
            _vm.SshConfigStore.MoveEntry(entry.Id, 1);
    }

    private void DeleteEntry(object sender, RoutedEventArgs e)
    {
        if (GetContextEntry(sender) is { } entry)
        {
            _vm.SshConfigStore.Delete(entry.Id);
            if (_vm.SelectedSshConfigId == entry.Id)
                _vm.SelectedSshConfigId = null;
        }
    }

    private SSHConfigEntry? GetContextEntry(object sender)
    {
        if (sender is MenuItem { DataContext: SSHConfigListItem item })
            return item.Entry;
        return null;
    }
}
