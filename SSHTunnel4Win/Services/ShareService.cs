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
        // Not a share string: parse it as an ssh command line instead.
        if (lines.Count == 0 || !lines[0].StartsWith("sshtunnel://")) return ParseCLI(raw);

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

    // CLI parsing

    /// <summary>Options that take a separate value which is preserved verbatim in AdditionalArgs.</summary>
    private static readonly HashSet<string> PassthroughValueOptions = new()
    {
        "-b", "-c", "-E", "-e", "-F", "-I", "-J", "-m", "-O", "-Q", "-S", "-W", "-w"
    };

    /// <summary>Parses an ssh command line (the format produced by BuildCLI) into a config.</summary>
    public static SSHTunnelConfig? ParseCLI(string input)
    {
        var tokens = Tokenize(input);
        if (tokens.Count > 0)
        {
            var first = tokens[0];
            if (first == "ssh" || first.EndsWith("/ssh") || first.EndsWith("\\ssh")
                || first.EndsWith("ssh.exe", StringComparison.OrdinalIgnoreCase))
                tokens.RemoveAt(0);
        }
        if (tokens.Count == 0) return null;

        var config = new SSHTunnelConfig();
        var extra = new List<string>();
        string? destination = null;
        var index = 0;

        while (index < tokens.Count)
        {
            var token = tokens[index];
            index++;

            if (!token.StartsWith("-") || token.Length < 2)
            {
                // First bare token is the destination; anything after it is a remote command.
                if (destination == null && token.Length > 0) destination = token;
                continue;
            }

            var flag = token[..2];
            var inline = token[2..];

            string? NextValue()
            {
                if (inline.Length > 0) return inline;
                if (index >= tokens.Count) return null;
                return tokens[index++];
            }

            switch (flag)
            {
                case "-p":
                    var portValue = NextValue();
                    if (portValue != null && ushort.TryParse(portValue, out var port)) config.Port = port;
                    break;
                case "-i":
                    var identity = NextValue();
                    if (identity != null)
                    {
                        config.AuthMethod = AuthMethod.IdentityFile;
                        config.IdentityFile = identity;
                    }
                    break;
                case "-l":
                    var login = NextValue();
                    if (login != null) config.Username = login;
                    break;
                case "-L":
                case "-R":
                case "-D":
                    var forward = NextValue();
                    var forwardType = ForwardType(flag);
                    if (forward != null && forwardType != null)
                    {
                        var entry = ParseForwardArgument(forward, forwardType.Value);
                        if (entry != null) config.Tunnels.Add(entry);
                    }
                    break;
                case "-o":
                    var option = NextValue();
                    if (option != null)
                    {
                        if (option.StartsWith("PreferredAuthentications=") && option.Contains("password"))
                            config.AuthMethod = AuthMethod.Password;
                        else
                            extra.AddRange(new[] { "-o", option });
                    }
                    break;
                case "-N":
                case "-v":
                    break; // always applied when launching
                default:
                    if (PassthroughValueOptions.Contains(flag))
                    {
                        var value = NextValue();
                        if (value != null) extra.AddRange(new[] { flag, value });
                    }
                    else
                    {
                        extra.Add(token);
                    }
                    break;
            }
        }

        // A tunnel command without any forwarding rule is not a tunnel config.
        if (destination == null || config.Tunnels.Count == 0) return null;

        var target = destination;
        if (target.StartsWith("ssh://")) target = target["ssh://".Length..];

        var atIndex = target.LastIndexOf('@');
        if (atIndex >= 0)
        {
            config.Username = target[..atIndex];
            target = target[(atIndex + 1)..];
        }

        var hostComponents = target.Split(':');
        if (hostComponents.Length == 2 && ushort.TryParse(hostComponents[1], out var hostPort))
        {
            config.Port = hostPort;
            target = hostComponents[0];
        }
        if (target.Length == 0) return null;

        config.Host = target;
        config.Name = target;
        config.AdditionalArgs = string.Join(" ", extra);
        return config;
    }

    /// <summary>Parses forwarding rules out of CLI-style text. Accepts "-L 8080:localhost:80",
    /// "-D1080", share lines like "L:8080:localhost:80", and a bare "8080:localhost:80"
    /// (treated as local). Non-forwarding tokens are ignored.</summary>
    public static List<TunnelEntry> ParseForwardingEntries(string input)
    {
        var tokens = Tokenize(input);
        var entries = new List<TunnelEntry>();
        var index = 0;

        while (index < tokens.Count)
        {
            var token = tokens[index];
            index++;

            if (token.StartsWith("-"))
            {
                if (token.Length < 2) continue;
                var type = ForwardType(token[..2]);
                if (type == null) continue;

                var value = token[2..];
                if (value.Length == 0)
                {
                    if (index >= tokens.Count) break;
                    value = tokens[index];
                    index++;
                }
                var entry = ParseForwardArgument(value, type.Value);
                if (entry != null) entries.Add(entry);
            }
            else
            {
                var entry = ParseTunnelLine(token) ?? ParseForwardArgument(token, TunnelType.Local);
                if (entry != null) entries.Add(entry);
            }
        }
        return entries;
    }

    private static TunnelType? ForwardType(string flag) => flag switch
    {
        "-L" => TunnelType.Local,
        "-R" => TunnelType.Remote,
        "-D" => TunnelType.Dynamic,
        _ => null
    };

    /// <summary>Parses "[bind:]port:host:hostport" (-L/-R) or "[bind:]port" (-D).</summary>
    private static TunnelEntry? ParseForwardArgument(string argument, TunnelType type)
    {
        var parts = argument.Split(':');
        var entry = new TunnelEntry { Type = type };

        if (type == TunnelType.Dynamic)
        {
            switch (parts.Length)
            {
                case 1:
                    if (!ushort.TryParse(parts[0], out var dynamicPort)) return null;
                    entry.LocalPort = dynamicPort;
                    break;
                case 2:
                    if (!ushort.TryParse(parts[1], out var boundDynamicPort)) return null;
                    entry.BindAddress = parts[0];
                    entry.LocalPort = boundDynamicPort;
                    break;
                default:
                    return null;
            }
            return entry;
        }

        switch (parts.Length)
        {
            case 3:
                if (!ushort.TryParse(parts[0], out var localPort) || !ushort.TryParse(parts[2], out var remotePort))
                    return null;
                entry.LocalPort = localPort;
                entry.RemoteHost = parts[1];
                entry.RemotePort = remotePort;
                break;
            case 4:
                if (!ushort.TryParse(parts[1], out var boundLocalPort) || !ushort.TryParse(parts[3], out var boundRemotePort))
                    return null;
                entry.BindAddress = parts[0];
                entry.LocalPort = boundLocalPort;
                entry.RemoteHost = parts[2];
                entry.RemotePort = boundRemotePort;
                break;
            default:
                return null;
        }

        return entry.RemoteHost.Length == 0 ? null : entry;
    }

    /// <summary>Splits a command line on whitespace, honoring quotes and line continuations.
    /// Backslashes are literal so that Windows paths survive round-tripping.</summary>
    private static List<string> Tokenize(string input)
    {
        var joined = input.Replace("\\\r\n", " ").Replace("\\\n", " ");
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var hasToken = false;
        char? quote = null;

        foreach (var character in joined)
        {
            if (quote != null)
            {
                if (character == quote) quote = null;
                else current.Append(character);
                hasToken = true;
                continue;
            }
            if (character == '"' || character == '\'')
            {
                quote = character;
                hasToken = true;
                continue;
            }
            if (char.IsWhiteSpace(character))
            {
                if (hasToken) tokens.Add(current.ToString());
                current.Clear();
                hasToken = false;
                continue;
            }
            current.Append(character);
            hasToken = true;
        }
        if (hasToken) tokens.Add(current.ToString());
        return tokens;
    }
}
