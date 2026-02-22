using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SSHTunnel4Win.Models;

namespace SSHTunnel4Win.Services;

public class SSHConfigStore
{
    public List<SSHConfigEntry> Entries { get; private set; } = new();
    public List<string> ConfigFiles { get; private set; } = new();
    public event Action? EntriesChanged;

    public SSHConfigStore()
    {
        Load();
    }

    public void Load()
    {
        Entries = SSHConfigParser.ParseAll();
        ConfigFiles = SSHConfigParser.ConfigFiles();
        EntriesChanged?.Invoke();
    }

    public void Update(SSHConfigEntry entry)
    {
        var index = Entries.FindIndex(e => e.Id == entry.Id);
        if (index < 0) return;
        var oldFile = Entries[index].SourceFile;
        Entries[index] = entry;
        SaveFile(entry.SourceFile);
        if (oldFile != entry.SourceFile)
            SaveFile(oldFile);
        EntriesChanged?.Invoke();
    }

    public void Add(SSHConfigEntry entry)
    {
        Entries.Add(entry);
        SaveFile(entry.SourceFile);
        EntriesChanged?.Invoke();
    }

    public void Delete(Guid id)
    {
        var entry = Entries.FirstOrDefault(e => e.Id == id);
        if (entry == null) return;
        var file = entry.SourceFile;
        Entries.RemoveAll(e => e.Id == id);
        SaveFile(file);
        EntriesChanged?.Invoke();
    }

    public void ToggleComment(Guid id)
    {
        var index = Entries.FindIndex(e => e.Id == id);
        if (index < 0) return;
        Entries[index].Commented = !Entries[index].Commented;
        SaveFile(Entries[index].SourceFile);
        EntriesChanged?.Invoke();
    }

    public void MoveEntry(Guid id, int direction)
    {
        var index = Entries.FindIndex(e => e.Id == id);
        if (index < 0) return;
        var file = Entries[index].SourceFile;
        var fileIndices = Entries.Select((e, i) => (e, i))
            .Where(x => x.e.SourceFile == file)
            .Select(x => x.i).ToList();
        var localPos = fileIndices.IndexOf(index);
        var targetLocalPos = localPos + direction;
        if (targetLocalPos < 0 || targetLocalPos >= fileIndices.Count) return;
        var targetIndex = fileIndices[targetLocalPos];
        (Entries[index], Entries[targetIndex]) = (Entries[targetIndex], Entries[index]);
        SaveFile(file);
        EntriesChanged?.Invoke();
    }

    public void MoveEntries(IEnumerable<Guid> ids, string targetFile)
    {
        var affectedFiles = new HashSet<string> { targetFile };
        foreach (var id in ids)
        {
            var index = Entries.FindIndex(e => e.Id == id);
            if (index < 0) continue;
            affectedFiles.Add(Entries[index].SourceFile);
            Entries[index].SourceFile = targetFile;
        }
        foreach (var file in affectedFiles)
            SaveFile(file);
        EntriesChanged?.Invoke();
    }

    private void SaveFile(string filePath)
    {
        var fileEntries = Entries.Where(e => e.SourceFile == filePath).ToList();
        var mainConfig = SSHConfigParser.ConfigFiles().FirstOrDefault() ?? "";

        if (filePath == mainConfig)
        {
            var header = PreserveMainConfigHeader(filePath);
            var body = SSHConfigParser.Serialize(fileEntries);
            File.WriteAllText(filePath, header + body);
        }
        else
        {
            var content = SSHConfigParser.Serialize(fileEntries);
            File.WriteAllText(filePath, content);
        }
    }

    private static string PreserveMainConfigHeader(string filePath)
    {
        if (!File.Exists(filePath)) return "";
        var existing = File.ReadAllText(filePath);
        var headerLines = new List<string>();
        foreach (var line in existing.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Host ", StringComparison.OrdinalIgnoreCase)) break;
            headerLines.Add(line);
        }
        if (headerLines.Count > 0 && !string.IsNullOrEmpty(headerLines.Last()))
            headerLines.Add("");
        return string.Join("\n", headerLines);
    }
}
