using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSHTunnel4Win.Resources;
using SSHTunnel4Win.Services;

namespace SSHTunnel4Win.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    public AppSettings Settings { get; }

    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private bool _isUpdateAvailable;

    private UpdateInfo? _latestUpdate;

    public string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public SettingsViewModel(AppSettings settings)
    {
        Settings = settings;
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        IsCheckingUpdate = true;
        IsUpdateAvailable = false;
        UpdateStatus = Strings.Checking;

        var info = await UpdateService.CheckForUpdateAsync();

        if (info != null)
        {
            _latestUpdate = info;
            UpdateStatus = string.Format(Strings.VersionAvailable, info.Version);
            IsUpdateAvailable = true;
        }
        else
        {
            _latestUpdate = null;
            UpdateStatus = Strings.UpToDate;
        }

        IsCheckingUpdate = false;
    }

    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_latestUpdate == null) return;

        if (_latestUpdate.InstallerUrl != null)
        {
            try
            {
                IsUpdateAvailable = false;
                UpdateStatus = Strings.Download + "...";
                await UpdateService.PerformUpdateAsync(_latestUpdate.InstallerUrl, _ => { });
            }
            catch
            {
                Process.Start(new ProcessStartInfo { FileName = _latestUpdate.HtmlUrl, UseShellExecute = true });
            }
        }
        else
        {
            Process.Start(new ProcessStartInfo { FileName = _latestUpdate.HtmlUrl, UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/TypoStudio/ssh-tunnel-for-win",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenBuyMeACoffee()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.buymeacoffee.com/typ0s2d10",
            UseShellExecute = true
        });
    }
}
