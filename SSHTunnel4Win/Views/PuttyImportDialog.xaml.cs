using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SSHTunnel4Win.Models;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;
using SSHTunnel4Win.ViewModels;

namespace SSHTunnel4Win.Views;

public class PuttySessionItem : INotifyPropertyChanged
{
    private bool _isChecked;

    public PuttySession Session { get; }
    /// <summary>Parsed key header (not decrypted), or null if the session has no usable PuTTY key.</summary>
    public PuttyKey? Key { get; }
    public string? KeyError { get; }
    public bool AlreadyImported { get; }

    public string Name => Session.Name;
    public bool IsEnabled => Session.CanImport;
    public string RulesText => Session.CanImport ? Session.Forwardings.Count.ToString() : "";

    public string Target
    {
        get
        {
            if (string.IsNullOrEmpty(Session.Host)) return "";
            var user = string.IsNullOrEmpty(Session.UserName) ? "" : Session.UserName + "@";
            var port = Session.Port == 22 ? "" : ":" + Session.Port;
            return user + Session.Host + port;
        }
    }

    public List<string> NoteList { get; } = new();
    public string Notes => string.Join("; ", NoteList);

    /// <summary>Anything that will need the user's attention after importing.</summary>
    public List<string> Attention { get; } = new();
    public bool HasWarning => IsEnabled && Attention.Count > 0;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value || (value && !IsEnabled)) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PuttySessionItem(PuttySession session, bool alreadyImported)
    {
        Session = session;
        AlreadyImported = alreadyImported;

        switch (session.Issue)
        {
            case PuttyImportIssue.DefaultSettings: NoteList.Add(Strings.PuttyIssueDefault); return;
            case PuttyImportIssue.NotSsh: NoteList.Add(string.Format(Strings.PuttyIssueNotSsh, session.Protocol)); return;
            case PuttyImportIssue.NoHost: NoteList.Add(Strings.PuttyIssueNoHost); return;
        }

        if (alreadyImported) NoteList.Add(Strings.PuttyAlreadyImported);

        if (session.HasKey && File.Exists(session.KeyFile) && PuttyKey.IsPuttyKeyFile(session.KeyFile))
        {
            try
            {
                Key = PuttyKey.Load(session.KeyFile);
                var fileName = Path.GetFileName(session.KeyFile);
                NoteList.Add(string.Format(Key.IsEncrypted ? Strings.PuttyNoteEncryptedKey : Strings.PuttyNoteKey, fileName));
                // Surface unusable key types now rather than at connect time
                Key.EnsureSupported();
            }
            catch (PuttyKeyException e)
            {
                KeyError = PuttyKeyMessages.Describe(e);
                Key = null;
                Attention.Add(KeyError);
            }
        }
        else if (session.HasKey && File.Exists(session.KeyFile))
        {
            NoteList.Add(string.Format(Strings.PuttyNoteKey, Path.GetFileName(session.KeyFile)));
        }

        Attention.AddRange(session.Warnings);
        if (session.Forwardings.Count == 0) NoteList.Add(Strings.PuttyNoteNoRules);
        NoteList.AddRange(Attention);
    }
}

public partial class PuttyImportDialog : Window
{
    private readonly MainViewModel _vm;
    private List<PuttySessionItem> _items = new();

    public PuttyImportDialog(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        var sources = PuttySessionImporter.AvailableSources();
        // Always offer PuTTY so the dialog can explain that nothing was found
        SourceBox.ItemsSource = sources.Count > 0 ? sources : new[] { PuttySource.PuTTY };
        SourceBox.SelectedIndex = 0;
        Loaded += (_, _) => SessionList.Focus();
    }

    private PuttySource Source => (PuttySource)SourceBox.SelectedItem;

    private void SourceChanged(object sender, SelectionChangedEventArgs e)
    {
        // Fires with no selection while the source list itself is being replaced
        if (SourceBox.SelectedItem is not PuttySource) return;
        foreach (var item in _items) item.PropertyChanged -= OnItemChanged;
        _items = PuttySessionImporter.ReadSessions(Source)
            .Select(s => new PuttySessionItem(s, PuttySessionImporter.IsAlreadyImported(s, _vm.ConfigStore.Configs)))
            .ToList();
        foreach (var item in _items)
        {
            // Pre-select everything that can be imported, except sessions imported before
            item.IsChecked = item.IsEnabled && !item.AlreadyImported;
            item.PropertyChanged += OnItemChanged;
        }
        SessionList.ItemsSource = _items;
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCount();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        var importable = _items.Count(i => i.IsEnabled);
        var selected = _items.Count(i => i.IsChecked);
        CountText.Text = string.Format(Strings.SelectedCount, selected, importable);
        ImportBtn.IsEnabled = selected > 0;
    }

    private void SelectAllClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items) item.IsChecked = item.IsEnabled;
    }

    private void SelectNoneClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items) item.IsChecked = false;
    }

    /// <summary>Space toggles every highlighted row, like a checked list box.</summary>
    private void ListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;
        var rows = SessionList.SelectedItems.Cast<PuttySessionItem>().Where(i => i.IsEnabled).ToList();
        if (rows.Count == 0) return;
        var check = !rows.All(i => i.IsChecked);
        foreach (var row in rows) row.IsChecked = check;
        e.Handled = true;
    }

    private void ListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SessionList.SelectedItem is PuttySessionItem { IsEnabled: true } item)
            item.IsChecked = !item.IsChecked;
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsChecked).ToList();
        if (selected.Count == 0) return;

        // One passphrase prompt per encrypted key file, even when several sessions share it
        var passphrases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skippedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in selected.Where(i => i.Key?.IsEncrypted == true)
                     .GroupBy(i => i.Session.KeyFile, StringComparer.OrdinalIgnoreCase))
        {
            var prompt = new PassphraseDialog(group.First().Key!, group.Key, group.Select(i => i.Name)) { Owner = this };
            if (prompt.ShowDialog() == true)
                passphrases[group.Key] = prompt.Passphrase;
            else
                skippedKeys.Add(group.Key);
        }

        var configs = selected.Select(i => PuttySessionImporter.ToTunnelConfig(i.Session)).ToList();
        _vm.ImportTunnels(configs);

        for (int i = 0; i < selected.Count; i++)
        {
            if (passphrases.TryGetValue(selected[i].Session.KeyFile, out var passphrase))
                CredentialService.SavePassword(passphrase, configs[i].Id);
        }

        HostKeyImportResult? hostKeys = null;
        if (HostKeysBox.IsChecked == true)
        {
            try
            {
                var keys = PuttyHostKeyImporter.ReadHostKeys(Source, selected.Select(i => (i.Session.Host, i.Session.Port)));
                hostKeys = PuttyHostKeyImporter.AppendToKnownHosts(keys);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Prompt.Warn(ex.Message, Strings.ImportHostKeys);
            }
        }

        ShowSummary(selected, skippedKeys, hostKeys);
        DialogResult = true;
    }

    private void ShowSummary(List<PuttySessionItem> imported, HashSet<string> skippedKeys, HostKeyImportResult? hostKeys)
    {
        var sb = new StringBuilder(string.Format(Strings.ImportSummary, imported.Count));
        if (hostKeys is { Added: > 0 })
            sb.Append('\n').Append(string.Format(Strings.ImportSummaryHostKeys, hostKeys.Added));
        if (hostKeys is { Conflicts: > 0 })
            sb.Append('\n').Append(string.Format(Strings.ImportSummaryHostKeyConflicts, hostKeys.Conflicts));

        var attention = imported
            .Select(i =>
            {
                var issues = new List<string>(i.Attention);
                if (skippedKeys.Contains(i.Session.KeyFile)) issues.Add(Strings.ImportSummaryNoPassphrase);
                return (i.Name, Issues: issues);
            })
            .Where(x => x.Issues.Count > 0)
            .ToList();
        if (attention.Count > 0)
        {
            const int maxLines = 12;
            sb.Append("\n\n").Append(Strings.ImportSummaryNeedsAttention);
            foreach (var (name, issues) in attention.Take(maxLines))
                sb.Append("\n• ").Append(name).Append(" — ").Append(string.Join("; ", issues));
            if (attention.Count > maxLines)
                sb.Append("\n• …(+").Append(attention.Count - maxLines).Append(')');
        }

        if (attention.Count > 0 || hostKeys is { Conflicts: > 0 })
            Prompt.Warn(sb.ToString(), Strings.ImportSessionsTitle);
        else
            Prompt.Info(sb.ToString(), Strings.ImportSessionsTitle);
    }
}
