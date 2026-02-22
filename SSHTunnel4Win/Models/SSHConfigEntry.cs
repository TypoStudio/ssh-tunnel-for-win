using System;
using System.Collections.Generic;
using System.Linq;

namespace SSHTunnel4Win.Models;

public class SSHConfigDirective
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class SSHConfigEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Host { get; set; } = "";
    public List<SSHConfigDirective> Directives { get; set; } = new();
    public string SourceFile { get; set; } = "";
    public string Comment { get; set; } = "";
    public bool Commented { get; set; }

    public static readonly string[] CommonKeys =
    {
        "HostName", "User", "Port", "IdentityFile",
        "ProxyCommand", "ProxyJump", "ForwardAgent",
        "ServerAliveInterval", "ServerAliveCountMax"
    };

    public string GetValue(string key)
    {
        var lower = key.ToLowerInvariant();
        return Directives.FirstOrDefault(d => d.Key.ToLowerInvariant() == lower)?.Value ?? "";
    }

    public void SetValue(string value, string key)
    {
        var lower = key.ToLowerInvariant();
        var idx = Directives.FindIndex(d => d.Key.ToLowerInvariant() == lower);
        if (idx >= 0)
        {
            if (string.IsNullOrEmpty(value))
                Directives.RemoveAt(idx);
            else
                Directives[idx].Value = value;
        }
        else if (!string.IsNullOrEmpty(value))
        {
            Directives.Add(new SSHConfigDirective { Key = key, Value = value });
        }
    }

    public List<SSHConfigDirective> OtherDirectives
    {
        get
        {
            var commonLower = new HashSet<string>(CommonKeys.Select(k => k.ToLowerInvariant()));
            return Directives
                .Where(d => !commonLower.Contains(d.Key.ToLowerInvariant()))
                .OrderBy(d => d.Key.ToLowerInvariant())
                .ToList();
        }
    }
}

public class SSHConfigHost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Hostname { get; set; } = "";
    public ushort Port { get; set; } = 22;
    public string User { get; set; } = "";
    public string IdentityFile { get; set; } = "";
}
