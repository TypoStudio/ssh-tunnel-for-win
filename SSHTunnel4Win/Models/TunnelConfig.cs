using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SSHTunnel4Win.Resources;

namespace SSHTunnel4Win.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TunnelType
{
    [JsonPropertyName("local")]
    Local,
    [JsonPropertyName("remote")]
    Remote,
    [JsonPropertyName("dynamic")]
    Dynamic
}

public static class TunnelTypeExtensions
{
    public static string Flag(this TunnelType type) => type switch
    {
        TunnelType.Local => "-L",
        TunnelType.Remote => "-R",
        TunnelType.Dynamic => "-D",
        _ => ""
    };

    public static string DisplayName(this TunnelType type) => type switch
    {
        TunnelType.Local => Strings.TunnelTypeLocal,
        TunnelType.Remote => Strings.TunnelTypeRemote,
        TunnelType.Dynamic => Strings.TunnelTypeDynamic,
        _ => ""
    };
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthMethod
{
    [JsonPropertyName("identityFile")]
    IdentityFile,
    [JsonPropertyName("password")]
    Password
}

public static class AuthMethodExtensions
{
    public static string DisplayName(this AuthMethod method) => method switch
    {
        AuthMethod.IdentityFile => Strings.IdentityFile,
        AuthMethod.Password => Strings.Password,
        _ => ""
    };
}

public class TunnelEntry
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("type")]
    public TunnelType Type { get; set; } = TunnelType.Local;

    [JsonPropertyName("localPort")]
    public ushort LocalPort { get; set; }

    [JsonPropertyName("remoteHost")]
    public string RemoteHost { get; set; } = "localhost";

    [JsonPropertyName("remotePort")]
    public ushort RemotePort { get; set; }

    [JsonPropertyName("bindAddress")]
    public string BindAddress { get; set; } = "";

    [JsonIgnore]
    public string SshArgument
    {
        get
        {
            var bind = string.IsNullOrEmpty(BindAddress) ? "" : $"{BindAddress}:";
            return Type switch
            {
                TunnelType.Local or TunnelType.Remote => $"{bind}{LocalPort}:{RemoteHost}:{RemotePort}",
                TunnelType.Dynamic => $"{bind}{LocalPort}",
                _ => ""
            };
        }
    }

    public TunnelEntry Clone() => new()
    {
        Id = Id,
        Type = Type,
        LocalPort = LocalPort,
        RemoteHost = RemoteHost,
        RemotePort = RemotePort,
        BindAddress = BindAddress
    };
}

public class SSHTunnelConfig
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public ushort Port { get; set; } = 22;

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("authMethod")]
    public AuthMethod AuthMethod { get; set; } = AuthMethod.IdentityFile;

    [JsonPropertyName("identityFile")]
    public string IdentityFile { get; set; } = "";

    [JsonPropertyName("tunnels")]
    public List<TunnelEntry> Tunnels { get; set; } = new();

    [JsonPropertyName("autoConnect")]
    public bool AutoConnect { get; set; }

    [JsonPropertyName("disconnectOnQuit")]
    public bool DisconnectOnQuit { get; set; } = true;

    [JsonPropertyName("autoReconnect")]
    public bool AutoReconnect { get; set; } = true;

    [JsonPropertyName("additionalArgs")]
    public string AdditionalArgs { get; set; } = "";

    public SSHTunnelConfig Clone() => new()
    {
        Id = Id,
        Name = Name,
        Host = Host,
        Port = Port,
        Username = Username,
        AuthMethod = AuthMethod,
        IdentityFile = IdentityFile,
        Tunnels = Tunnels.ConvertAll(t => t.Clone()),
        AutoConnect = AutoConnect,
        DisconnectOnQuit = DisconnectOnQuit,
        AutoReconnect = AutoReconnect,
        AdditionalArgs = AdditionalArgs
    };
}
