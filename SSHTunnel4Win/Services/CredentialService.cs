using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SSHTunnel4Win.Services;

public static class CredentialService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SSHTunnel", "credentials.dat");

    public static void SavePassword(string password, Guid configId)
    {
        var store = LoadStore();
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password),
            null,
            DataProtectionScope.CurrentUser);
        store[configId.ToString()] = Convert.ToBase64String(encrypted);
        SaveStore(store);
    }

    public static string? GetPassword(Guid configId)
    {
        var store = LoadStore();
        if (!store.TryGetValue(configId.ToString(), out var b64)) return null;
        try
        {
            var encrypted = Convert.FromBase64String(b64);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    public static void DeletePassword(Guid configId)
    {
        var store = LoadStore();
        store.Remove(configId.ToString());
        SaveStore(store);
    }

    public static bool HasPassword(Guid configId) => GetPassword(configId) != null;

    private static Dictionary<string, string> LoadStore()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                var json = File.ReadAllText(FilePath);
                var store = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                try
                {
                    using var rkey = Registry.CurrentUser.OpenSubKey(@"Software\TypoStudio\SSHTunnel");
                    if (rkey?.GetValue("CredentialBackup") == null)
                        BackupCredentialsToRegistry(json);
                }
                catch { }
                return store;
            }
            catch
            {
                return new();
            }
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\TypoStudio\SSHTunnel");
            if (key?.GetValue("CredentialBackup") is string json)
            {
                var store = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                if (store.Count > 0) SaveStore(store);
                return store;
            }
        }
        catch { }
        return new();
    }

    private static void SaveStore(Dictionary<string, string> store)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
        BackupCredentialsToRegistry(json);
    }

    private static void BackupCredentialsToRegistry(string json)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\TypoStudio\SSHTunnel");
            key.SetValue("CredentialBackup", json);
        }
        catch { }
    }
}
