using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Windows;
using System.Windows.Controls;

namespace SSHTunnel4Win.Views;

public class SSHProcessInfo
{
    public int Pid { get; set; }
    public string CommandLine { get; set; } = "";
}

public partial class ProcessListDialog : Window
{
    public ProcessListDialog()
    {
        InitializeComponent();
        RefreshProcesses();
    }

    private void RefreshProcesses()
    {
        var processes = new List<SSHProcessInfo>();
        try
        {
            foreach (var proc in Process.GetProcessesByName("ssh"))
            {
                try
                {
                    var cmdLine = GetCommandLine(proc.Id);
                    if (!string.IsNullOrEmpty(cmdLine) &&
                        (cmdLine.Contains("-N") || cmdLine.Contains("-L") || cmdLine.Contains("-R") || cmdLine.Contains("-D")))
                    {
                        processes.Add(new SSHProcessInfo { Pid = proc.Id, CommandLine = cmdLine });
                    }
                }
                catch { }
            }
        }
        catch { }

        ProcessGrid.ItemsSource = processes;
        if (processes.Count == 0)
        {
            ProcessGrid.ItemsSource = null;
        }
    }

    private static string GetCommandLine(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            var obj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            return obj?["CommandLine"]?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private void RefreshClick(object sender, RoutedEventArgs e) => RefreshProcesses();

    private void KillAllClick(object sender, RoutedEventArgs e)
    {
        if (ProcessGrid.ItemsSource is List<SSHProcessInfo> processes)
        {
            foreach (var p in processes)
            {
                try { Process.GetProcessById(p.Pid).Kill(); } catch { }
            }
            RefreshProcesses();
        }
    }

    private void KillClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int pid })
        {
            try { Process.GetProcessById(pid).Kill(); } catch { }
            RefreshProcesses();
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
