using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            Configs = JsonSerializer.Deserialize<List<SSHTunnelConfig>>(json, JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load configs: {ex.Message}");
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save configs: {ex.Message}");
        }
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
