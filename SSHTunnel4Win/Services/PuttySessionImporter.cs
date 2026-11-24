using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using SSHTunnel4Win.Models;

namespace SSHTunnel4Win.Services;

/// <summary>A PuTTY-compatible client that keeps saved sessions in the registry.</summary>
public sealed record PuttySource(string Name, string RegistryPath)
{
    public static readonly PuttySource PuTTY = new("PuTTY", @"Software\SimonTatham\PuTTY");
    public static readonly PuttySource KiTTY = new("KiTTY", @"Software\9bis.com\KiTTY");

    public string SessionsPath => RegistryPath + @"\Sessions";
    public string HostKeysPath => RegistryPath + @"\SshHostKeys";

    public bool HasSessions()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SessionsPath);
            return key != null && key.SubKeyCount > 0;
        }
        catch
        {
            return false;
        }
    }
}

public enum PuttyImportIssue
{
    None,
    DefaultSettings,
    NotSsh,
    NoHost
}

/// <summary>One saved PuTTY session, plus what importing it would produce.</summary>
public sealed class PuttySession
{
    public string Name { get; init; } = "";
    public string Host { get; init; } = "";
    public string UserName { get; init; } = "";
    public int Port { get; init; } = 22;
    public string Protocol { get; init; } = "ssh";
    public string KeyFile { get; init; } = "";
    public List<TunnelEntry> Forwardings { get; init; } = new();
    public List<string> ExtraArgs { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public PuttyImportIssue Issue { get; init; }

    public bool CanImport => Issue == PuttyImportIssue.None;
    public bool HasKey => !string.IsNullOrEmpty(KeyFile);
}

/// <summary>
/// Reads PuTTY (or KiTTY) saved sessions from the registry and maps them onto tunnel configs.
/// Formats follow PuTTY's settings.c / winstore.c.
/// </summary>
public static class PuttySessionImporter
{
    private const string DefaultSettingsName = "Default Settings";

    // PuTTY's CONF_proxy_type values (putty.h)
    private const int ProxyNone = 0, ProxySshTcpip = 6;

    public static IReadOnlyList<PuttySource> AvailableSources() =>
        new[] { PuttySource.PuTTY, PuttySource.KiTTY }.Where(s => s.HasSessions()).ToList();

    public static List<PuttySession> ReadSessions(PuttySource source)
    {
        var raw = new List<(string Name, RegistryKey Key)>();
        using var sessionsKey = Registry.CurrentUser.OpenSubKey(source.SessionsPath);
        if (sessionsKey == null) return new();

        try
        {
            foreach (var sub in sessionsKey.GetSubKeyNames())
            {
                var key = sessionsKey.OpenSubKey(sub);
                if (key != null) raw.Add((UnescapeName(sub), key));
            }

            // Jump hosts may refer to another saved session by name
            var byName = raw.ToDictionary(r => r.Name, r => r.Key, StringComparer.OrdinalIgnoreCase);
            return raw
                .Select(r => ReadSession(r.Name, r.Key, byName))
                .OrderBy(s => s.CanImport ? 0 : 1)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        finally
        {
            foreach (var (_, key) in raw) key.Dispose();
        }
    }

    private static PuttySession ReadSession(string name, RegistryKey key, IReadOnlyDictionary<string, RegistryKey> all)
    {
        var warnings = new List<string>();
        var protocol = GetString(key, "Protocol", "ssh");
        if (protocol == "default") protocol = "ssh";

        var (user, host) = SplitUserHost(GetString(key, "HostName"));
        if (string.IsNullOrEmpty(user)) user = GetString(key, "UserName");
        var port = GetInt(key, "PortNumber", 22);
        if (port is <= 0 or > 65535) port = 22;

        var issue = name == DefaultSettingsName ? PuttyImportIssue.DefaultSettings
            : protocol != "ssh" ? PuttyImportIssue.NotSsh
            : string.IsNullOrEmpty(host) ? PuttyImportIssue.NoHost
            : PuttyImportIssue.None;

        var keyFile = GetString(key, "PublicKeyFile").Trim();
        if (!string.IsNullOrEmpty(keyFile) && !File.Exists(keyFile))
            warnings.Add(Resources.Strings.PuttyWarnKeyMissing);
        if (string.IsNullOrEmpty(keyFile) && issue == PuttyImportIssue.None)
            warnings.Add(Resources.Strings.PuttyWarnNoKey);

        var extraArgs = new List<string>();
        if (GetInt(key, "Compression", 0) != 0) extraArgs.Add("-C");
        if (GetInt(key, "AgentFwd", 0) != 0) extraArgs.Add("-A");
        if (GetInt(key, "LocalPortAcceptAll", 0) != 0) extraArgs.Add("-g");

        var proxyMethod = GetInt(key, "ProxyMethod", ProxyNone);
        if (proxyMethod == ProxySshTcpip)
        {
            var jump = ResolveJumpHost(key, all);
            if (jump != null) extraArgs.Add("-J " + jump);
        }
        else if (proxyMethod > ProxyNone)
        {
            warnings.Add(Resources.Strings.PuttyWarnProxy);
        }

        var forwardings = ParsePortForwardings(GetString(key, "PortForwardings"), out var badRules);
        if (badRules > 0)
            warnings.Add(string.Format(Resources.Strings.PuttyWarnBadRules, badRules));

        return new PuttySession
        {
            Name = name,
            Host = host,
            UserName = user,
            Port = port,
            Protocol = protocol,
            KeyFile = keyFile,
            Forwardings = forwardings,
            ExtraArgs = extraArgs,
            Warnings = warnings,
            Issue = issue
        };
    }

    /// <summary>
    /// PuTTY's "SSH" proxy type (0.77+) takes a host or the name of another saved session.
    /// Returns an OpenSSH -J destination.
    /// </summary>
    private static string? ResolveJumpHost(RegistryKey key, IReadOnlyDictionary<string, RegistryKey> all)
    {
        var proxyHost = GetString(key, "ProxyHost");
        if (string.IsNullOrEmpty(proxyHost)) return null;
        var user = GetString(key, "ProxyUsername");
        var port = GetInt(key, "ProxyPort", 22);

        if (all.TryGetValue(proxyHost, out var jumpSession))
        {
            var (jumpUser, jumpHost) = SplitUserHost(GetString(jumpSession, "HostName"));
            if (!string.IsNullOrEmpty(jumpHost))
            {
                proxyHost = jumpHost;
                if (string.IsNullOrEmpty(user))
                    user = string.IsNullOrEmpty(jumpUser) ? GetString(jumpSession, "UserName") : jumpUser;
                port = GetInt(jumpSession, "PortNumber", 22);
            }
        }

        var userPart = string.IsNullOrEmpty(user) ? "" : user + "@";
        var portPart = port is 22 or <= 0 ? "" : ":" + port;
        return userPart + proxyHost + portPart;
    }

    /// <summary>
    /// Parses PuTTY's PortForwardings value, e.g. "L8080=localhost:80,4R9000=db:5432,D1080=".
    /// Separators inside keys/values are backslash-escaped (settings.c wmap/gppmap).
    /// </summary>
    public static List<TunnelEntry> ParsePortForwardings(string raw, out int invalidCount)
    {
        var result = new List<TunnelEntry>();
        invalidCount = 0;
        foreach (var (rawKey, value) in SplitEscapedMap(raw))
        {
            var key = rawKey;
            // Optional address-family prefix: '4' (IPv4 only) or '6' (IPv6 only)
            if (key.Length > 0 && key[0] is '4' or '6') key = key[1..];
            if (key.Length < 2) { invalidCount++; continue; }

            var typeChar = key[0];
            var (bind, listenPort) = SplitHostPort(key[1..]);
            if (listenPort <= 0) { invalidCount++; continue; }

            switch (typeChar)
            {
                case 'D':
                    result.Add(new TunnelEntry { Type = TunnelType.Dynamic, LocalPort = (ushort)listenPort, BindAddress = bind });
                    break;
                case 'L':
                case 'R':
                {
                    var (destHost, destPort) = SplitHostPort(value);
                    if (string.IsNullOrEmpty(destHost) || destPort <= 0) { invalidCount++; continue; }
                    result.Add(new TunnelEntry
                    {
                        Type = typeChar == 'L' ? TunnelType.Local : TunnelType.Remote,
                        LocalPort = (ushort)listenPort,
                        BindAddress = bind,
                        RemoteHost = destHost,
                        RemotePort = (ushort)destPort
                    });
                    break;
                }
                default:
                    invalidCount++;
                    break;
            }
        }
        return result;
    }

    private static IEnumerable<(string Key, string Value)> SplitEscapedMap(string raw)
    {
        if (string.IsNullOrEmpty(raw)) yield break;
        var key = new StringBuilder();
        var value = new StringBuilder();
        var inValue = false;
        for (int i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\\' && i + 1 < raw.Length)
            {
                (inValue ? value : key).Append(raw[++i]);
            }
            else if (c == '=' && !inValue)
            {
                inValue = true;
            }
            else if (c == ',')
            {
                if (key.Length > 0) yield return (key.ToString(), value.ToString());
                key.Clear(); value.Clear(); inValue = false;
            }
            else
            {
                (inValue ? value : key).Append(c);
            }
        }
        if (key.Length > 0) yield return (key.ToString(), value.ToString());
    }

    /// <summary>Splits "port", "host:port" or "[v6]:port"; returns ("", -1) on failure.</summary>
    private static (string Host, int Port) SplitHostPort(string text)
    {
        text = text.Trim();
        var colon = text.LastIndexOf(':');
        // A bare port, or an unbracketed IPv6 address without a port
        if (colon < 0 || (text.IndexOf(':') != colon && !text.StartsWith('[')))
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var only) && only is > 0 and <= 65535
                ? ("", only) : ("", -1);
        var host = text[..colon];
        return int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535
            ? (host, port) : ("", -1);
    }

    private static (string User, string Host) SplitUserHost(string hostName)
    {
        hostName = hostName.Trim();
        var at = hostName.LastIndexOf('@');
        return at < 0 ? ("", hostName) : (hostName[..at], hostName[(at + 1)..]);
    }

    /// <summary>Builds the tunnel config for an importable session.</summary>
    public static SSHTunnelConfig ToTunnelConfig(PuttySession session) => new()
    {
        Name = session.Name,
        Host = session.Host,
        Port = (ushort)session.Port,
        Username = session.UserName,
        // PuTTY never stores passwords; sessions without a key almost always log in with one
        AuthMethod = session.HasKey ? AuthMethod.IdentityFile : AuthMethod.Password,
        IdentityFile = session.KeyFile,
        Tunnels = session.Forwardings.Select(t => t.Clone()).ToList(),
        AdditionalArgs = string.Join(" ", session.ExtraArgs)
    };

    /// <summary>True if a tunnel for the same session already exists (e.g. imported earlier).</summary>
    public static bool IsAlreadyImported(PuttySession session, IEnumerable<SSHTunnelConfig> existing) =>
        existing.Any(c =>
            string.Equals(c.Name, session.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Host, session.Host, StringComparison.OrdinalIgnoreCase) &&
            c.Port == session.Port);

    /// <summary>Reverses PuTTY's escape_registry_key (%XX byte escapes, in the ANSI code page).</summary>
    public static string UnescapeName(string escaped)
    {
        if (!escaped.Contains('%')) return escaped;
        var bytes = new List<byte>();
        for (int i = 0; i < escaped.Length; i++)
        {
            if (escaped[i] == '%' && i + 2 < escaped.Length &&
                byte.TryParse(escaped.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                bytes.Add(b);
                i += 2;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(escaped[i].ToString()));
            }
        }
        var array = bytes.ToArray();
        try
        {
            return new UTF8Encoding(false, true).GetString(array);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(array);
            }
            catch
            {
                return Encoding.Latin1.GetString(array);
            }
        }
    }

    private static string GetString(RegistryKey key, string name, string fallback = "") =>
        key.GetValue(name) is string s ? s : fallback;

    private static int GetInt(RegistryKey key, string name, int fallback) =>
        key.GetValue(name) switch
        {
            int i => i,
            string s when int.TryParse(s, out var p) => p,
            _ => fallback
        };
}
