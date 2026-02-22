using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    private readonly TunnelStatus _status;

    public Dictionary<Guid, string> Logs { get; } = new();
    public event Action<Guid>? LogChanged;

    public SSHProcessManager(TunnelStatus status)
    {
        _status = status;
    }

    public List<ushort> CheckPortConflicts(SSHTunnelConfig config)
    {
        var conflicts = new List<ushort>();
        foreach (var entry in config.Tunnels)
        {
            if (entry.LocalPort == 0) continue;
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

        _status.SetState(id, ConnectionState.Connecting);

        var sshExe = FindSshExe();
        Logs[id] = ""; // Initialize log before BuildArguments (which may append warnings)
        var args = BuildArguments(config);
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

        // SSH_ASKPASS for password auth
        if (config.AuthMethod == AuthMethod.Password)
        {
            var password = CredentialService.GetPassword(config.Id);
            if (password != null)
            {
                var askpass = CreateAskPassScript(password, config.Id);
                process.StartInfo.EnvironmentVariables["SSH_ASKPASS"] = askpass;
                process.StartInfo.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
                process.StartInfo.EnvironmentVariables["DISPLAY"] = "localhost:0";
            }
        }

        process.EnableRaisingEvents = true;

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
                    var msg = $"Connection failed (exit {process.ExitCode})";
                    if (!string.IsNullOrEmpty(lastLog)) msg += "\n" + lastLog;
                    _status.SetState(id, ConnectionState.Error, msg);
                }
                else
                {
                    _status.SetState(id, ConnectionState.Disconnected);
                }
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
            Logs[id] = (Logs.TryGetValue(id, out var errLog) ? errLog : "") + $"ERROR: {ex.Message}\n";
            LogChanged?.Invoke(id);
            _status.SetState(id, ConnectionState.Error, ex.Message);
        }
    }

    public void Disconnect(Guid configId)
    {
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
        foreach (var id in _processes.Keys.ToList())
            Disconnect(id);
    }

    public void DisconnectOnQuit(IEnumerable<SSHTunnelConfig> configs)
    {
        foreach (var config in configs.Where(c => c.DisconnectOnQuit))
            Disconnect(config.Id);
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
        var escaped = password.Replace("\"", "\\\"");
        File.WriteAllText(scriptPath, $"@echo off\r\necho {escaped}\r\n");
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
