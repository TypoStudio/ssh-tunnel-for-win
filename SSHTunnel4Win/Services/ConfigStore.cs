using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using SSHTunnel4Win.Models;

namespace SSHTunnel4Win.Services;

public class ConfigStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public List<SSHTunnelConfig> Configs { get; private set; } = new();
    public event Action? ConfigsChanged;

    public ConfigStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "SSHTunnel");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "tunnels.json");
        Load();
    }

    public void Load()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                Configs = JsonSerializer.Deserialize<List<SSHTunnelConfig>>(json, JsonOptions) ?? new();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load configs: {ex.Message}");
            }
            EnsureBackup();
        }
        else
        {
            RestoreFromRegistry();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Configs, JsonOptions);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
            BackupToRegistry(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save configs: {ex.Message}");
        }
    }

    private void EnsureBackup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\TypoStudio\SSHTunnel");
            if (key?.GetValue("ConfigBackup") == null)
                BackupToRegistry(JsonSerializer.Serialize(Configs, JsonOptions));
        }
        catch { }
    }

    private static void BackupToRegistry(string json)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\TypoStudio\SSHTunnel");
            key.SetValue("ConfigBackup", json);
        }
        catch { }
    }

    private void RestoreFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\TypoStudio\SSHTunnel");
            if (key?.GetValue("ConfigBackup") is string json)
            {
                Configs = JsonSerializer.Deserialize<List<SSHTunnelConfig>>(json, JsonOptions) ?? new();
                if (Configs.Count > 0) Save();
            }
        }
        catch { }
    }

    public void Add(SSHTunnelConfig config)
    {
        Configs.Add(config);
        Save();
        ConfigsChanged?.Invoke();
    }

    public void Update(SSHTunnelConfig config)
    {
        var index = Configs.FindIndex(c => c.Id == config.Id);
        if (index < 0) return;
        Configs[index] = config;
        Save();
        ConfigsChanged?.Invoke();
    }

    public void Delete(Guid id)
    {
        Configs.RemoveAll(c => c.Id == id);
        Save();
        ConfigsChanged?.Invoke();
    }

    public SSHTunnelConfig? FindById(Guid id) => Configs.FirstOrDefault(c => c.Id == id);
}
