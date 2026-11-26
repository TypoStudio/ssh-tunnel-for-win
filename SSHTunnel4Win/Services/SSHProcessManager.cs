using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SSHTunnel4Win.Models;

namespace SSHTunnel4Win.Services;

public class SSHProcessManager
{
    private readonly Dictionary<Guid, Process> _processes = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _connectTimers = new();
    private readonly Dictionary<Guid, string> _tempKeyFiles = new();
    // Plaintext OpenSSH keys converted from .ppk files; deleted as soon as ssh has authenticated
    private readonly Dictionary<Guid, string> _convertedKeyFiles = new();
    private readonly TunnelStatus _status;

    // Auto-reconnect state
    private readonly HashSet<Guid> _manualDisconnects = new();
    private readonly HashSet<Guid> _pendingReconnect = new();
    private readonly Dictionary<Guid, SSHTunnelConfig> _reconnectConfigs = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _reconnectTimers = new();
    private readonly Dictionary<Guid, int> _retryCounts = new();
    private readonly Dictionary<Guid, SSHTunnelConfig> _pendingImmediateReconnect = new();
    private bool _isNetworkAvailable = true;

    public Dictionary<Guid, string> Logs { get; } = new();
    public event Action<Guid>? LogChanged;

    public SSHProcessManager(TunnelStatus status)
    {
        _status = status;
        DeleteStaleSecretFiles();
        _isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var wasAvailable = _isNetworkAvailable;
            _isNetworkAvailable = e.IsAvailable;
            if (!wasAvailable && e.IsAvailable)
                ReconnectPending();
        });
    }

    public List<ushort> CheckPortConflicts(SSHTunnelConfig config)
    {
        var conflicts = new List<ushort>();
        foreach (var entry in config.Tunnels)
        {
            // -R listens on the SSH server, not on this PC, so a local bind test says nothing about it
            if (entry.LocalPort == 0 || entry.Type == TunnelType.Remote) continue;
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, entry.LocalPort));
            }
            catch (SocketException)
            {
                conflicts.Add(entry.LocalPort);
            }
        }
        return conflicts;
    }

    public void Connect(SSHTunnelConfig config)
    {
        var id = config.Id;
        if (_status.GetState(id).IsActive()) return;

        _reconnectConfigs[id] = config;
        _pendingReconnect.Remove(id);
        CancelReconnectTimer(id);
        _retryCounts.Remove(id);
        _manualDisconnects.Remove(id);

        _status.SetState(id, ConnectionState.Connecting);

        var sshExe = FindSshExe();
        Logs[id] = ""; // Initialize log before BuildArguments (which may append warnings)
        List<string> args;
        try
        {
            args = BuildArguments(config);
        }
        catch (Exception e) when (e is PuttyKeyException or IOException or UnauthorizedAccessException)
        {
            // A key problem (e.g. missing passphrase) is reported up front instead of an obscure ssh failure
            var message = e is PuttyKeyException pk ? PuttyKeyMessages.Describe(pk) : e.Message;
            AppendLog(id, $"ERROR: {message}\n");
            _status.SetState(id, ConnectionState.Error, message);
            return;
        }
        var cmdLine = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

        // Prepend command line to any warnings from BuildArguments
        var warnings = Logs.TryGetValue(id, out var preLog) ? preLog : "";
        Logs[id] = $"$ {sshExe} {cmdLine}\n\n{warnings}";

        var process = new Process();
        process.StartInfo.FileName = sshExe;
        process.StartInfo.Arguments = cmdLine;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardInput = true;

        // SSH_ASKPASS answers the password prompt, or the passphrase prompt of an encrypted
        // OpenSSH key (.ppk keys are decrypted by the app, so ssh never asks for those)
        if (config.AuthMethod == AuthMethod.Password ||
            (config.AuthMethod == AuthMethod.IdentityFile && !_convertedKeyFiles.ContainsKey(id)))
        {
            var password = CredentialService.GetPassword(config.Id);
            if (!string.IsNullOrEmpty(password))
            {
                var askpass = CreateAskPassScript(password, config.Id);
                process.StartInfo.EnvironmentVariables["SSH_ASKPASS"] = askpass;
                process.StartInfo.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
                process.StartInfo.EnvironmentVariables["DISPLAY"] = "localhost:0";
            }
        }

        process.EnableRaisingEvents = true;

        // With a jump host every hop logs "Authenticated to", and the hops come before ssh reads our
        // key for the target. So wait until ssh has offered the key, then for the next success line.
        var convertedKeyName = _convertedKeyFiles.TryGetValue(id, out var convertedKey) ? Path.GetFileName(convertedKey) : null;
        var keyOffered = false;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Logs[id] = (Logs.TryGetValue(id, out var log) ? log : "") + e.Data + "\n";
                LogChanged?.Invoke(id);
            });
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // The plaintext copy of a .ppk key is no longer needed once ssh has used it
                if (convertedKeyName != null)
                {
                    if (!keyOffered)
                        keyOffered = IsKeyOfferLine(e.Data, convertedKeyName);
                    else if (e.Data.Contains("Authenticated to"))
                        CleanupConvertedKeyFile(id);
                }
                Logs[id] = (Logs.TryGetValue(id, out var log) ? log : "") + e.Data + "\n";
                LogChanged?.Invoke(id);
            });
        };

        process.Exited += (_, _) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                CleanupAskPassScript(id);
                CleanupTempKeyFile(id);
                CleanupConvertedKeyFile(id);
                if (_connectTimers.TryGetValue(id, out var cts))
                {
                    cts.Cancel();
                    _connectTimers.Remove(id);
                }
                _processes.Remove(id);

                if (process.ExitCode == 0)
                {
                    _status.SetState(id, ConnectionState.Disconnected);
                }
                else if (_status.GetState(id) == ConnectionState.Connecting)
                {
                    // Include last lines of SSH log in error message
                    var lastLog = "";
                    if (Logs.TryGetValue(id, out var fullLog) && !string.IsNullOrEmpty(fullLog))
                    {
                        var lines = fullLog.TrimEnd('\n').Split('\n');
                        lastLog = string.Join("\n", lines.Length > 5 ? lines[^5..] : lines);
                    }
                    var msg = string.Format(Resources.Strings.ConnectionFailedExit, process.ExitCode);
                    if (!string.IsNullOrEmpty(lastLog)) msg += "\n" + lastLog;
                    _status.SetState(id, ConnectionState.Error, msg);
                }
                else
                {
                    _status.SetState(id, ConnectionState.Disconnected);
                    // Auto-reconnect on unexpected disconnect
                    if (!_manualDisconnects.Contains(id)
                        && _reconnectConfigs.TryGetValue(id, out var cfg)
                        && cfg.AutoReconnect)
                    {
                        _pendingReconnect.Add(id);
                        if (_isNetworkAvailable)
                            ScheduleReconnect(id);
                    }
                }
                _manualDisconnects.Remove(id);
                if (_pendingImmediateReconnect.Remove(id, out var reconnectCfg))
                    Connect(reconnectCfg);
            });
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _processes[id] = process;

            // Timer-based connection detection: 3 seconds
            var cts = new CancellationTokenSource();
            _connectTimers[id] = cts;
            Task.Delay(3000, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (_processes.TryGetValue(id, out var p) && !p.HasExited)
                        _status.SetState(id, ConnectionState.Connected);
                });
            });
        }
        catch (Exception ex)
        {
            CleanupConvertedKeyFile(id);
            Logs[id] = (Logs.TryGetValue(id, out var errLog) ? errLog : "") + $"ERROR: {ex.Message}\n";
            LogChanged?.Invoke(id);
            _status.SetState(id, ConnectionState.Error, ex.Message);
        }
    }

    public void Disconnect(Guid configId)
    {
        _manualDisconnects.Add(configId);
        _pendingReconnect.Remove(configId);
        CancelReconnectTimer(configId);
        _retryCounts.Remove(configId);

        if (_connectTimers.TryGetValue(configId, out var cts))
        {
            cts.Cancel();
            _connectTimers.Remove(configId);
        }

        if (_processes.TryGetValue(configId, out var process) && !process.HasExited)
        {
            try { process.Kill(); } catch { }
        }
        else
        {
            _status.SetState(configId, ConnectionState.Disconnected);
        }
    }

    public void Toggle(SSHTunnelConfig config)
    {
        if (_status.GetState(config.Id).IsActive())
            Disconnect(config.Id);
        else
            Connect(config);
    }

    public void DisconnectAll()
    {
        // Include tunnels waiting to auto-reconnect, otherwise they come back on their own
        foreach (var id in _processes.Keys.Union(_pendingReconnect).ToList())
            Disconnect(id);
    }

    public void DisconnectOnQuit(IEnumerable<SSHTunnelConfig> configs)
    {
        foreach (var config in configs.Where(c => c.DisconnectOnQuit))
            Disconnect(config.Id);
    }

    public void Reconnect(SSHTunnelConfig config)
    {
        var id = config.Id;
        if (!_status.GetState(id).IsActive())
        {
            Connect(config);
            return;
        }
        _pendingImmediateReconnect[id] = config;
        Disconnect(id);
    }

    public void ReconnectAll()
    {
        foreach (var (id, config) in _reconnectConfigs.ToList())
        {
            if (_status.GetState(id).IsActive())
                Reconnect(config);
        }
    }

    // Auto-reconnect helpers

    private void ScheduleReconnect(Guid id)
    {
        CancelReconnectTimer(id);
        var count = _retryCounts.GetValueOrDefault(id, 0);
        int[] delays = [3000, 5000, 10000, 30000, 60000];
        var delay = delays[Math.Min(count, delays.Length - 1)];
        var cts = new CancellationTokenSource();
        _reconnectTimers[id] = cts;
        Task.Delay(delay, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!_pendingReconnect.Contains(id)) return;
                if (!_reconnectConfigs.TryGetValue(id, out var cfg)) return;
                _retryCounts[id] = count + 1;
                Connect(cfg);
            });
        });
    }

    private void ReconnectPending()
    {
        foreach (var id in _pendingReconnect.ToList())
        {
            _retryCounts[id] = 0;
            ScheduleReconnect(id);
        }
    }

    private void CancelReconnectTimer(Guid id)
    {
        if (_reconnectTimers.TryGetValue(id, out var cts))
        {
            cts.Cancel();
            _reconnectTimers.Remove(id);
        }
    }

    public void CancelReconnect(Guid configId)
    {
        _pendingReconnect.Remove(configId);
        CancelReconnectTimer(configId);
        _retryCounts.Remove(configId);
        _reconnectConfigs.Remove(configId);
    }

    private List<string> BuildArguments(SSHTunnelConfig config)
    {
        var args = new List<string>
        {
            "-N",
            "-v",
            "-o", "ExitOnForwardFailure=yes",
            "-o", "ServerAliveInterval=30",
            "-o", "ServerAliveCountMax=3",
            "-o", "StrictHostKeyChecking=accept-new",
            "-p", config.Port.ToString()
        };

        switch (config.AuthMethod)
        {
            case AuthMethod.IdentityFile:
                if (!string.IsNullOrEmpty(config.IdentityFile))
                {
                    var keyPath = ResolveKeyFile(config.IdentityFile, config.Id);
                    args.AddRange(new[] { "-i", keyPath });
                }
                args.AddRange(new[] { "-o", "PasswordAuthentication=no" });
                // Connect answers the key's passphrase prompt through SSH_ASKPASS, which would answer a
                // server's keyboard-interactive question just the same and so send it the passphrase
                if (!_convertedKeyFiles.ContainsKey(config.Id) && !string.IsNullOrEmpty(CredentialService.GetPassword(config.Id)))
                    args.AddRange(new[] { "-o", "KbdInteractiveAuthentication=no" });
                break;
            case AuthMethod.Password:
                args.AddRange(new[] { "-o", "PreferredAuthentications=password,keyboard-interactive" });
                break;
        }

        foreach (var entry in config.Tunnels)
            args.AddRange(new[] { entry.Type.Flag(), entry.SshArgument });

        if (!string.IsNullOrEmpty(config.AdditionalArgs))
        {
            var extra = config.AdditionalArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            args.AddRange(extra);
        }

        args.Add($"{config.Username}@{config.Host}");
        return args;
    }

    private static string FindSshExe()
    {
        // Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir, "ssh.exe");
            if (File.Exists(candidate)) return candidate;
        }
        // Fallback to System32
        var system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
        if (File.Exists(system32)) return system32;
        return "ssh.exe";
    }

    private static string CreateAskPassScript(string password, Guid configId)
    {
        var tempDir = Path.GetTempPath();
        var scriptPath = Path.Combine(tempDir, $"sshtunnel-askpass-{configId}.cmd");
        // cmd.exe metacharacters must be caret-escaped (and % doubled) or the secret is mangled;
        // "echo(" prints the text verbatim even if it is empty or looks like "on"/"off"
        var escaped = new StringBuilder();
        foreach (var c in password)
        {
            if (c is '^' or '&' or '|' or '<' or '>' or '(' or ')' or '"') escaped.Append('^');
            if (c == '%') escaped.Append('%');
            escaped.Append(c);
        }
        File.WriteAllText(scriptPath, $"@echo off\r\necho({escaped}\r\n");
        return scriptPath;
    }

    private static void CleanupAskPassScript(Guid configId)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"sshtunnel-askpass-{configId}.cmd");
        try { File.Delete(scriptPath); } catch { }
    }

    /// <summary>
    /// If key file is on a UNC/network path, copy to local temp with restricted permissions.
    /// Windows OpenSSH cannot verify NTFS ACLs on network paths and rejects the key.
    /// </summary>
    private string ResolveKeyFile(string keyPath, Guid configId)
    {
        // Windows OpenSSH can't read PuTTY keys: convert to a short-lived, user-only OpenSSH key
        if (PuttyKey.IsPuttyKeyFile(keyPath))
            return ConvertPuttyKey(keyPath, configId);

        // Detect UNC/network paths: \\server\share or \server\share
        var normalized = keyPath.Replace("/", @"\");
        var isUnc = normalized.StartsWith(@"\");

        // Also detect mapped drives pointing to network (e.g. Z:\)
        if (!isUnc)
        {
            try
            {
                var fullPath = Path.GetFullPath(normalized);
                // Check if the resolved path is UNC
                if (fullPath.StartsWith(@"\\"))
                {
                    normalized = fullPath;
                    isUnc = true;
                }
            }
            catch { }
        }

        if (!isUnc)
            return keyPath;

        // Ensure proper UNC prefix (\\server\share)
        if (!normalized.StartsWith(@"\\"))
            normalized = @"\" + normalized;

        AppendLog(configId, $"[INFO] Key file is on network path: {normalized}\n");

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "sshtunnel-keys");
            Directory.CreateDirectory(tempDir);
            var fileName = Path.GetFileName(normalized);
            var tempFile = Path.Combine(tempDir, $"{configId}_{fileName}");

            // Reuse existing temp file if already copied
            if (File.Exists(tempFile))
            {
                AppendLog(configId, $"[INFO] Using existing local key: {tempFile}\n");
                _tempKeyFiles[configId] = tempFile;
                return tempFile;
            }

            if (!File.Exists(normalized))
            {
                AppendLog(configId, $"[WARN] Key file not found: {normalized}\n");
                return keyPath;
            }

            File.Copy(normalized, tempFile);

            // Restrict permissions: remove inheritance, only current user gets full control
            RunCmd("icacls", $"\"{tempFile}\" /inheritance:r /grant:r \"{Environment.UserName}:(F)\"");

            _tempKeyFiles[configId] = tempFile;
            return tempFile;
        }
        catch (Exception ex)
        {
            AppendLog(configId, $"[WARN] Failed to copy key file locally: {ex.Message}\n");
            return keyPath;
        }
    }

    private string ConvertPuttyKey(string keyPath, Guid configId)
    {
        var key = PuttyKey.Load(keyPath);
        var passphrase = key.IsEncrypted ? CredentialService.GetPassword(configId) : null;
        var pem = key.ToOpenSshPrivateKey(passphrase);
        var path = SecureKeyFile.Write($"{configId}.key", pem);
        _convertedKeyFiles[configId] = path;
        AppendLog(configId, $"[INFO] Using PuTTY key {Path.GetFileName(keyPath)} ({key.Algorithm})\n");
        return path;
    }

    private void CleanupConvertedKeyFile(Guid configId)
    {
        if (_convertedKeyFiles.Remove(configId, out var path))
            SecureKeyFile.Delete(path);
    }

    /// <summary>True for the ssh -v lines printed when user authentication starts using the given key file.</summary>
    private static bool IsKeyOfferLine(string line, string keyFileName) =>
        line.Contains(keyFileName, StringComparison.OrdinalIgnoreCase) &&
        (line.Contains("Will attempt key") || line.Contains("Offering public key") || line.Contains("Trying private key"));

    /// <summary>
    /// Removes the secrets this run wrote to disk. Needed on exit because the process-exit handlers
    /// are dispatched to the UI thread, which no longer runs them once the app is shutting down.
    /// </summary>
    public void CleanupSecretFiles()
    {
        foreach (var id in _convertedKeyFiles.Keys.ToList())
            CleanupConvertedKeyFile(id);
        foreach (var id in _processes.Keys)
            CleanupAskPassScript(id);
    }

    /// <summary>A crash or a kill skips every cleanup, so secrets of an earlier run may still be in the temp folder.</summary>
    private static void DeleteStaleSecretFiles()
    {
        try
        {
            foreach (var script in Directory.EnumerateFiles(Path.GetTempPath(), "sshtunnel-askpass-*.cmd"))
            {
                try { File.Delete(script); } catch { }
            }

            if (!Directory.Exists(SecureKeyFile.Directory)) return;
            // Only converted keys ("{id}.key"): "{id}_{name}" files are copies of network keys that get reused
            foreach (var key in Directory.EnumerateFiles(SecureKeyFile.Directory, "*.key"))
            {
                if (Guid.TryParse(Path.GetFileNameWithoutExtension(key), out _))
                    SecureKeyFile.Delete(key);
            }
        }
        catch { }
    }

    public void ClearLog(Guid configId)
    {
        Logs.Remove(configId);
        LogChanged?.Invoke(configId);
    }

    private void AppendLog(Guid configId, string message)
    {
        Logs[configId] = (Logs.TryGetValue(configId, out var l) ? l : "") + message;
        LogChanged?.Invoke(configId);
    }

    private void CleanupTempKeyFile(Guid configId)
    {
        if (_tempKeyFiles.TryGetValue(configId, out var path))
        {
            try
            {
                RunCmd("takeown", $"/f \"{path}\"");
                RunCmd("icacls", $"\"{path}\" /grant \"{Environment.UserName}:(F)\"");
                File.Delete(path);
            }
            catch { }
            _tempKeyFiles.Remove(configId);
        }
    }

    private static void RunCmd(string fileName, string arguments)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        proc?.WaitForExit(5000);
    }
}
