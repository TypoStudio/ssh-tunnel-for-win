using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public class SSHConfigListItem
{
    public SSHConfigEntry Entry { get; }
    public string Host => (Entry.Commented ? "# " : "") + Entry.Host;
    public bool IsDisabled => Entry.Commented;
    public string FileName => Path.GetFileName(Entry.SourceFile);

    public string Subtitle
    {
        get
        {
            var hostName = Entry.GetValue("HostName");
            return string.IsNullOrEmpty(hostName) ? FileName : $"{hostName} · {FileName}";
        }
    }

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
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedSshConfigId))
                SyncSelection();
        };
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
        // An empty search result isn't the same as having no hosts, so only show the hint for the latter
        EmptyState.Visibility = _allItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncSelection();
    }

    /// <summary>Re-selects the current entry after the list is rebuilt (every edit rewrites the store).</summary>
    private void SyncSelection()
    {
        var item = (ConfigListBox.ItemsSource as List<SSHConfigListItem>)?
            .FirstOrDefault(i => i.Entry.Id == _vm.SelectedSshConfigId);
        if (ConfigListBox.SelectedItem != item)
            ConfigListBox.SelectedItem = item;
        if (item != null)
            ConfigListBox.ScrollIntoView(item);
    }

    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConfigListBox.SelectedItem is SSHConfigListItem item)
            _vm.SelectedSshConfigId = item.Entry.Id;
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && ConfigListBox.SelectedItem is SSHConfigListItem item)
        {
            ConfirmAndDelete(item.Entry);
            e.Handled = true;
        }
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
            ConfirmAndDelete(entry);
    }

    private void ConfirmAndDelete(SSHConfigEntry entry)
    {
        // This rewrites the user's ssh config file, so always ask first
        var message = string.Format(Strings.ConfirmDeleteHost, entry.Host, Path.GetFileName(entry.SourceFile));
        if (!Prompt.Confirm(message, Strings.Delete)) return;
        _vm.SshConfigStore.Delete(entry.Id);
        if (_vm.SelectedSshConfigId == entry.Id)
            _vm.SelectedSshConfigId = null;
    }

    private static SSHConfigEntry? GetContextEntry(object sender)
    {
        if (sender is MenuItem { DataContext: SSHConfigListItem item })
            return item.Entry;
        return null;
    }
}
