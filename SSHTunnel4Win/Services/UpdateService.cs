using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SSHTunnel4Win.Services;

public class UpdateInfo
{
    public string Version { get; init; } = "";
    public string HtmlUrl { get; init; } = "";
    public string? InstallerUrl { get; init; }
}

public static class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/TypoStudio/ssh-tunnel-for-win/releases/latest";

    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("User-Agent", "SSHTunnel4Win");
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync(ApiUrl);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json);
            if (release == null) return null;

            var remoteVersion = release.TagName.StartsWith("v")
                ? release.TagName[1..]
                : release.TagName;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

            if (!IsNewer(remoteVersion, currentVersion)) return null;

            var installerUrl = release.Assets
                .FirstOrDefault(a => a.Name.EndsWith(".exe") || a.Name.EndsWith(".msi"))
                ?.BrowserDownloadUrl;

            return new UpdateInfo
            {
                Version = remoteVersion,
                HtmlUrl = release.HtmlUrl,
                InstallerUrl = installerUrl
            };
        }
        catch
        {
            return null;
        }
    }

    public static async Task PerformUpdateAsync(string downloadUrl, Action<double> progressHandler)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var tempPath = Path.Combine(Path.GetTempPath(), $"SSHTunnel-update{Path.GetExtension(downloadUrl)}");

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(tempPath);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalRead += bytesRead;
            if (totalBytes > 0)
                progressHandler((double)totalRead / totalBytes);
        }

        fileStream.Close();

        // Run installer and exit
        Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            UseShellExecute = true
        });

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            System.Windows.Application.Current.Shutdown();
        });
    }

    private static bool IsNewer(string remote, string current)
    {
        var r = remote.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var c = current.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();

        for (int i = 0; i < Math.Max(r.Length, c.Length); i++)
        {
            var rv = i < r.Length ? r[i] : 0;
            var cv = i < c.Length ? c[i] : 0;
            if (rv > cv) return true;
            if (rv < cv) return false;
        }
        return false;
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
