using System;
using System.Collections.Generic;
using System.Linq;
using SSHTunnel4Win.Models;

namespace SSHTunnel4Win.Services;

public static class ShareService
{
    public static string Encode(SSHTunnelConfig config)
    {
        var name = Uri.EscapeDataString(config.Name);
        var lines = new List<string>
        {
            $"sshtunnel://{config.Username}@{config.Host}:{config.Port}/{name}"
        };

        foreach (var entry in config.Tunnels)
        {
            switch (entry.Type)
            {
                case TunnelType.Local:
                    lines.Add($"L:{entry.LocalPort}:{entry.RemoteHost}:{entry.RemotePort}");
                    break;
                case TunnelType.Remote:
                    lines.Add($"R:{entry.LocalPort}:{entry.RemoteHost}:{entry.RemotePort}");
                    break;
                case TunnelType.Dynamic:
                    lines.Add($"D:{entry.LocalPort}");
                    break;
            }
        }

        return string.Join("\n", lines);
    }

    public static string BuildCLI(SSHTunnelConfig config)
    {
        var args = new List<string> { "ssh", "-N" };

        if (config.Port != 22)
            args.AddRange(new[] { "-p", config.Port.ToString() });

        switch (config.AuthMethod)
        {
            case AuthMethod.IdentityFile:
                if (!string.IsNullOrEmpty(config.IdentityFile))
                    args.AddRange(new[] { "-i", config.IdentityFile });
                break;
            case AuthMethod.Password:
                args.AddRange(new[] { "-o", "PreferredAuthentications=password,keyboard-interactive" });
                break;
        }

        foreach (var entry in config.Tunnels)
            args.AddRange(new[] { entry.Type.Flag(), entry.SshArgument });

        if (!string.IsNullOrEmpty(config.AdditionalArgs))
            args.Add(config.AdditionalArgs);

        args.Add($"{config.Username}@{config.Host}");
        return string.Join(" ", args);
    }

    public static SSHTunnelConfig? Decode(string input)
    {
        var raw = input.Trim();

        // Support legacy base64 format
        if (raw.StartsWith("sshtunnel://") && !raw.Contains('@'))
            return DecodeLegacyBase64(raw);

        var lines = raw.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
        if (lines.Count == 0 || !lines[0].StartsWith("sshtunnel://")) return null;

        var header = lines[0];
        var uri = header["sshtunnel://".Length..];

        var atIndex = uri.IndexOf('@');
        if (atIndex < 0) return null;
        var user = uri[..atIndex];
        var afterAt = uri[(atIndex + 1)..];

        var hostPart = afterAt;
        var name = "";
        var slashIndex = afterAt.IndexOf('/');
        if (slashIndex >= 0)
        {
            hostPart = afterAt[..slashIndex];
            name = Uri.UnescapeDataString(afterAt[(slashIndex + 1)..]);
        }

        var hostComponents = hostPart.Split(':', 2);
        var host = hostComponents[0];
        ushort port = hostComponents.Length > 1 && ushort.TryParse(hostComponents[1], out var p) ? p : (ushort)22;

        var tunnels = new List<TunnelEntry>();
        foreach (var line in lines.Skip(1))
        {
            var entry = ParseTunnelLine(line);
            if (entry != null) tunnels.Add(entry);
        }

        return new SSHTunnelConfig
        {
            Id = Guid.NewGuid(),
            Name = name,
            Host = host,
            Port = port,
            Username = user,
            Tunnels = tunnels
        };
    }

    private static TunnelEntry? ParseTunnelLine(string line)
    {
        var parts = line.Split(':', 4);
        if (parts.Length < 2) return null;

        var typeStr = parts[0].ToUpperInvariant();
        var entry = new TunnelEntry();

        switch (typeStr)
        {
            case "L":
                if (parts.Length != 4 || !ushort.TryParse(parts[1], out var ll) || !ushort.TryParse(parts[3], out var lr))
                    return null;
                entry.Type = TunnelType.Local;
                entry.LocalPort = ll;
                entry.RemoteHost = parts[2];
                entry.RemotePort = lr;
                break;
            case "R":
                if (parts.Length != 4 || !ushort.TryParse(parts[1], out var rl) || !ushort.TryParse(parts[3], out var rr))
                    return null;
                entry.Type = TunnelType.Remote;
                entry.LocalPort = rl;
                entry.RemoteHost = parts[2];
                entry.RemotePort = rr;
                break;
            case "D":
                if (!ushort.TryParse(parts[1], out var dl)) return null;
                entry.Type = TunnelType.Dynamic;
                entry.LocalPort = dl;
                break;
            default:
                return null;
        }
        return entry;
    }

    private static SSHTunnelConfig? DecodeLegacyBase64(string raw)
    {
        try
        {
            var base64 = raw["sshtunnel://".Length..];
            var data = Convert.FromBase64String(base64);
            var json = System.Text.Encoding.UTF8.GetString(data);
            var config = System.Text.Json.JsonSerializer.Deserialize<SSHTunnelConfig>(json);
            if (config != null) config.Id = Guid.NewGuid();
            return config;
        }
        catch
        {
            return null;
        }
    }
}
