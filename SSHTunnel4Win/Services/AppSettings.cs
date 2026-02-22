using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;

namespace SSHTunnel4Win.Services;

public partial class AppSettings : ObservableObject
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SSHTunnel", "settings.json");

    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private bool _openManagerOnLaunch;
    [ObservableProperty] private bool _autoCheckForUpdates = true;

    public AppSettings()
    {
        Load();
    }

    partial void OnLaunchAtLoginChanged(bool value)
    {
        Save();
        UpdateRegistryAutoStart(value);
    }

    partial void OnOpenManagerOnLaunchChanged(bool value) => Save();
    partial void OnAutoCheckForUpdatesChanged(bool value) => Save();

    private void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data == null) return;
            _launchAtLogin = data.LaunchAtLogin;
            _openManagerOnLaunch = data.OpenManagerOnLaunch;
            _autoCheckForUpdates = data.AutoCheckForUpdates;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load settings: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var data = new SettingsData
            {
                LaunchAtLogin = LaunchAtLogin,
                OpenManagerOnLaunch = OpenManagerOnLaunch,
                AutoCheckForUpdates = AutoCheckForUpdates
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private static void UpdateRegistryAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (enable)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null)
                    key.SetValue("SSHTunnel", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("SSHTunnel", throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update registry: {ex.Message}");
        }
    }

    private class SettingsData
    {
        public bool LaunchAtLogin { get; set; }
        public bool OpenManagerOnLaunch { get; set; }
        public bool AutoCheckForUpdates { get; set; } = true;
    }
}
