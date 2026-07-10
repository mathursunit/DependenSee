using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.App.Services;

namespace ServiceMap.App.ViewModels;

/// <summary>GUI settings plus collector service control.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly Action _onSettingsChanged;

    [ObservableProperty] private string _databasePath;
    [ObservableProperty] private int _refreshIntervalSeconds;
    [ObservableProperty] private string _exportDirectory;
    [ObservableProperty] private bool _openFolderAfterExport;

    [ObservableProperty] private string _serviceStatus = "—";
    [ObservableProperty] private bool _serviceControlSupported;

    public SettingsViewModel(AppSettings settings, Action onSettingsChanged)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;
        _databasePath = settings.DatabasePath;
        _refreshIntervalSeconds = settings.RefreshIntervalSeconds;
        _exportDirectory = settings.ExportDirectory;
        _openFolderAfterExport = settings.OpenFolderAfterExport;
        ShellHelper.OpenAfterExport = settings.OpenFolderAfterExport;
        ServiceControlSupported = WindowsServiceControl.IsSupported;
        RefreshServiceStatus();
    }

    /// <summary>Locate the install/uninstall scripts relative to the running app.</summary>
    private static string ScriptPath(string leaf)
    {
        var baseDir = AppContext.BaseDirectory;
        // Try a few likely locations: alongside the app, or the repo scripts folder.
        var candidates = new[]
        {
            Path.Combine(baseDir, "scripts", leaf),
            Path.Combine(baseDir, leaf),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "scripts", leaf))
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    [RelayCommand]
    private void ApplySettings()
    {
        _settings.DatabasePath = DatabasePath;
        _settings.RefreshIntervalSeconds = Math.Max(1, RefreshIntervalSeconds);
        _settings.ExportDirectory = ExportDirectory;
        _settings.OpenFolderAfterExport = OpenFolderAfterExport;
        ShellHelper.OpenAfterExport = OpenFolderAfterExport;
        _settings.Save();
        _onSettingsChanged();
    }

    [RelayCommand]
    private void RefreshServiceStatus() =>
        ServiceStatus = WindowsServiceControl.IsSupported
            ? WindowsServiceControl.QueryStatus()
            : "Windows only";

    [RelayCommand]
    private void StartService()
    {
        WindowsServiceControl.Start();
        DelayedStatusRefresh();
    }

    [RelayCommand]
    private void StopService()
    {
        WindowsServiceControl.Stop();
        DelayedStatusRefresh();
    }

    [RelayCommand]
    private void InstallService()
    {
        WindowsServiceControl.Install(ScriptPath("install-service.ps1"));
        DelayedStatusRefresh();
    }

    [RelayCommand]
    private void UninstallService()
    {
        WindowsServiceControl.Uninstall(ScriptPath("uninstall-service.ps1"));
        DelayedStatusRefresh();
    }

    private void DelayedStatusRefresh()
    {
        // Service transitions take a moment; refresh status shortly after.
        Task.Delay(2500).ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshServiceStatus);
        });
    }
}
