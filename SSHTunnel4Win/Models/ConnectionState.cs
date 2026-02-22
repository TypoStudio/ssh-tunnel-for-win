using System;
using System.Collections.Generic;

namespace SSHTunnel4Win.Models;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

public static class ConnectionStateExtensions
{
    public static bool IsActive(this ConnectionState state) =>
        state is ConnectionState.Connecting or ConnectionState.Connected;
}

public class TunnelStatus
{
    private readonly Dictionary<Guid, ConnectionState> _states = new();
    private readonly Dictionary<Guid, string> _errorMessages = new();

    public event Action<Guid>? StateChanged;

    public ConnectionState GetState(Guid id) =>
        _states.TryGetValue(id, out var s) ? s : ConnectionState.Disconnected;

    public string GetErrorMessage(Guid id) =>
        _errorMessages.TryGetValue(id, out var m) ? m : "";

    public void SetState(Guid id, ConnectionState state, string errorMessage = "")
    {
        _states[id] = state;
        if (!string.IsNullOrEmpty(errorMessage))
            _errorMessages[id] = errorMessage;
        else
            _errorMessages.Remove(id);
        StateChanged?.Invoke(id);
    }
}
