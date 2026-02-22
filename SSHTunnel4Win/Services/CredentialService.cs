using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
        if (!File.Exists(FilePath)) return new();
        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void SaveStore(Dictionary<string, string> store)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
