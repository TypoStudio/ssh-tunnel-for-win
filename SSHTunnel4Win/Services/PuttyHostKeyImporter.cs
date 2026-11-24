using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SSHTunnel4Win.Services;

public sealed record KnownHostKey(string Host, int Port, string KeyType, string Base64)
{
    /// <summary>known_hosts host field: plain for port 22, "[host]:port" otherwise.</summary>
    public string HostField => Port == 22 ? Host : $"[{Host}]:{Port}";
    public string Line => $"{HostField} {KeyType} {Base64}";
}

public sealed record HostKeyImportResult(int Added, int AlreadyKnown, int Conflicts);

/// <summary>
/// Converts PuTTY's cached host keys (registry "SshHostKeys") into OpenSSH known_hosts entries,
/// so that imported sessions keep trusting exactly the servers PuTTY already verified.
/// Value formats follow PuTTY's sshrsa.c (rsastr_fmt) and sshecc.c (ecc_cache_str_shared).
/// </summary>
public static class PuttyHostKeyImporter
{
    public static string DefaultKnownHostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "known_hosts");

    /// <summary>Reads cached keys for the given (host, port) pairs. Unsupported key types are skipped.</summary>
    public static List<KnownHostKey> ReadHostKeys(PuttySource source, IEnumerable<(string Host, int Port)> hosts)
    {
        var wanted = new HashSet<(string, int)>(hosts.Select(h => (h.Host.ToLowerInvariant(), h.Port)));
        var result = new List<KnownHostKey>();
        using var key = Registry.CurrentUser.OpenSubKey(source.HostKeysPath);
        if (key == null) return result;

        foreach (var name in key.GetValueNames())
        {
            // Value name: "<cache id>@<port>:<escaped host>"
            var at = name.IndexOf('@');
            var colon = at < 0 ? -1 : name.IndexOf(':', at);
            if (at <= 0 || colon < 0) continue;
            if (!int.TryParse(name.AsSpan(at + 1, colon - at - 1), out var port)) continue;
            var host = PuttySessionImporter.UnescapeName(name[(colon + 1)..]).ToLowerInvariant();
            if (!wanted.Contains((host, port))) continue;

            if (key.GetValue(name) is not string value) continue;
            var converted = Convert(name[..at], value);
            if (converted != null)
                result.Add(new KnownHostKey(host, port, converted.Value.KeyType, converted.Value.Base64));
        }
        return result;
    }

    /// <summary>Converts one PuTTY cache entry; returns null for types OpenSSH can't use.</summary>
    public static (string KeyType, string Base64)? Convert(string cacheId, string value)
    {
        try
        {
            var parts = value.Split(',');
            var w = new SshWriter();
            switch (cacheId)
            {
                case "rsa2" when parts.Length == 2:
                    w.WriteString("ssh-rsa");
                    w.WriteMpint(ParseHex(parts[0]));
                    w.WriteMpint(ParseHex(parts[1]));
                    return ("ssh-rsa", System.Convert.ToBase64String(w.ToArray()));

                case "ssh-ed25519" when parts.Length == 2:
                {
                    // RFC 8032 point encoding: y little-endian, sign of x in the top bit
                    var x = ToBigInteger(parts[0]);
                    var y = ParseHex(parts[1]);
                    var encoded = new byte[32];
                    var yLen = Math.Min(y.Length, 32);
                    for (int i = 0; i < yLen; i++)
                        encoded[i] = y[y.Length - 1 - i];
                    if (!x.IsEven) encoded[31] |= 0x80;
                    w.WriteString("ssh-ed25519");
                    w.WriteString(encoded);
                    return ("ssh-ed25519", System.Convert.ToBase64String(w.ToArray()));
                }

                case "ecdsa-sha2-nistp256" or "ecdsa-sha2-nistp384" or "ecdsa-sha2-nistp521" when parts.Length == 3:
                {
                    var curve = parts[0];
                    var size = curve switch { "nistp256" => 32, "nistp384" => 48, "nistp521" => 66, _ => 0 };
                    if (size == 0 || cacheId != "ecdsa-sha2-" + curve) return null;
                    var point = new byte[1 + 2 * size];
                    point[0] = 0x04; // uncompressed
                    CopyRightAligned(ParseHex(parts[1]), point, 1, size);
                    CopyRightAligned(ParseHex(parts[2]), point, 1 + size, size);
                    w.WriteString(cacheId);
                    w.WriteString(curve);
                    w.WriteString(point);
                    return (cacheId, System.Convert.ToBase64String(w.ToArray()));
                }

                default:
                    return null;
            }
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Appends keys that known_hosts doesn't already have. A host that already has a key of the
    /// same type is left alone (counted as a conflict if the key differs) rather than overridden.
    /// </summary>
    public static HostKeyImportResult AppendToKnownHosts(IEnumerable<KnownHostKey> keys, string? knownHostsPath = null)
    {
        var path = knownHostsPath ?? DefaultKnownHostsPath;
        var existing = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
        var entries = existing.Select(ParseLine).Where(e => e != null).Select(e => e!.Value).ToList();

        int added = 0, known = 0, conflicts = 0;
        var toAdd = new List<string>();
        foreach (var key in keys.Distinct())
        {
            var match = entries.Where(e => e.KeyType == key.KeyType && HostMatches(e.Hosts, key.HostField)).ToList();
            if (match.Count == 0)
            {
                toAdd.Add(key.Line);
                entries.Add((new[] { key.HostField }, key.KeyType, key.Base64));
                added++;
            }
            else if (match.Any(m => m.Key == key.Base64)) known++;
            else conflicts++;
        }

        if (toAdd.Count > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var prefix = existing.Length > 0 && !File.ReadAllText(path).EndsWith('\n') ? "\n" : "";
            File.AppendAllText(path, prefix + string.Join("\n", toAdd) + "\n");
        }
        return new HostKeyImportResult(added, known, conflicts);
    }

    private static (string[] Hosts, string KeyType, string Key)? ParseLine(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) return null;
        var fields = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length > 0 && fields[0].StartsWith('@')) fields = fields[1..]; // @cert-authority / @revoked
        if (fields.Length < 3) return null;
        return (fields[0].Split(','), fields[1], fields[2]);
    }

    private static bool HostMatches(string[] patterns, string hostField) =>
        patterns.Any(p => p.StartsWith("|1|") ? HashedMatches(p, hostField)
            : string.Equals(p, hostField, StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks an OpenSSH hashed host entry: |1|base64(salt)|base64(HMAC-SHA1(salt, host)).</summary>
    private static bool HashedMatches(string pattern, string hostField)
    {
        var parts = pattern.Split('|');
        if (parts.Length != 4) return false;
        try
        {
            var salt = System.Convert.FromBase64String(parts[2]);
            var hash = System.Convert.FromBase64String(parts[3]);
            var computed = HMACSHA1.HashData(salt, Encoding.ASCII.GetBytes(hostField));
            return CryptographicOperations.FixedTimeEquals(hash, computed);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static BigInteger ToBigInteger(string hex) =>
        new(ParseHex(hex), isUnsigned: true, isBigEndian: true);

    /// <summary>Parses PuTTY's "0x..." hex (no fixed width, no leading zeros) into big-endian bytes.</summary>
    private static byte[] ParseHex(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) throw new FormatException();
        var hex = text[2..];
        if (hex.Length % 2 == 1) hex = "0" + hex;
        return System.Convert.FromHexString(hex);
    }

    private static void CopyRightAligned(byte[] source, byte[] target, int offset, int width)
    {
        // Drop any leading zero bytes beyond the field width, then right-align
        var start = Math.Max(0, source.Length - width);
        var len = source.Length - start;
        Buffer.BlockCopy(source, start, target, offset + width - len, len);
    }
}
