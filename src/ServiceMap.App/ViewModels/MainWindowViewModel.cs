using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ServiceMap.App.Services;
using ServiceMap.Core.Storage;

namespace ServiceMap.App.ViewModels;

/// <summary>
/// Root view model. Owns settings, the shared read-only data accessor, the GUI
/// workspace store, the tab view models, and the auto-refresh timer.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly DataAccess _data;
    private readonly WorkspaceStore _workspace;
    private readonly MultiSourceDataAccess _multi;
    private readonly DispatcherTimer _timer;

    public DashboardViewModel Dashboard { get; }
    public HistoryViewModel History { get; }
    public AnnotationsViewModel Annotations { get; }
    public FleetViewModel Fleet { get; }
    public MapViewModel Map { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private string _title = "Carrier DependenSee — See what connects.";

    public MainWindowViewModel()
    {
        _settings = AppSettings.Load();
        _data = new DataAccess(() => _settings.DatabasePath);
        _workspace = new WorkspaceStore(WorkspacePath());
        _multi = new MultiSourceDataAccess(_workspace);

        Dashboard = new DashboardViewModel(_data);
        History = new HistoryViewModel(_data, () => _settings.ExportDirectory)
        {
            AnnotationsProvider = () => _workspace.GetAnnotationLookup()
        };
        Annotations = new AnnotationsViewModel(_workspace);
        Fleet = new FleetViewModel(_multi);
        Map = new MapViewModel(_data, _multi);
        Settings = new SettingsViewModel(_settings, OnSettingsChanged);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.RefreshIntervalSeconds))
        };
        _timer.Tick += (_, _) => Dashboard.Refresh();
        _timer.Start();
        Dashboard.Refresh();
    }

    /// <summary>Called once the window exists so dialogs (save/open) are available.</summary>
    public void AttachDialogs(IDialogService dialogs)
    {
        History.SaveService = dialogs;
        Fleet.OpenService = dialogs;
    }

    private void OnSettingsChanged()
    {
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.RefreshIntervalSeconds));
        Dashboard.Refresh();
        Settings.RefreshServiceStatusCommand.Execute(null);
    }

    private static string WorkspacePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CarrierDependenSee");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "workspace.db");
    }
}
