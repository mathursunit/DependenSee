using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.App.Services;
using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;
using ServiceMap.Core.Analysis;

namespace ServiceMap.App.ViewModels;

/// <summary>One entry in the active-machine picker: the local machine or a fleet member.</summary>
public sealed record MachineOption(string Name, string? DbPath)
{
    public bool IsLocal => DbPath is null;
    public override string ToString() => Name;
}

/// <summary>
/// Root view model. Owns settings, the shared read-only data accessor, the GUI
/// workspace store, the tab view models, and the auto-refresh timer.
/// The "active machine" selection lets Dashboard/History/Map/PDF point at any
/// collected server (local or a fleet/remote snapshot) instead of only local.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly DataAccess _data;
    private readonly WorkspaceStore _workspace;
    private readonly MultiSourceDataAccess _multi;
    private readonly DispatcherTimer _timer;
    private IDialogService? _dialogs;

    // null => the local collector database; otherwise a fleet machine's db.
    private string? _activeDbPath;
    private bool _ready;

    public DashboardViewModel Dashboard { get; }
    public HistoryViewModel History { get; }
    public AnnotationsViewModel Annotations { get; }
    public FleetViewModel Fleet { get; }
    public RemoteViewModel Remote { get; }
    public MapViewModel Map { get; }
    public FirewallViewModel Firewall { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<MachineOption> MachineOptions { get; } = new();

    [ObservableProperty] private string _title = "Carrier DependenSee — See what connects.";
    [ObservableProperty] private MachineOption? _selectedMachineOption;
    [ObservableProperty] private bool _isRemoteActive;
    [ObservableProperty] private bool _isLocalActive = true;
    [ObservableProperty] private string _activeMachineBanner = string.Empty;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private string _exportStage = string.Empty;

    /// <summary>
    /// True when a local collector is present (its database exists or the service
    /// is installed) and the user hasn't forced console mode. When false the app
    /// is a pure viewer: the This-Machine tabs are hidden and it opens on Fleet.
    /// </summary>
    [ObservableProperty] private bool _thisMachineAvailable = true;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _modeBanner = string.Empty;

    public MainWindowViewModel()
    {
        _settings = AppSettings.Load();
        _data = new DataAccess(() => _activeDbPath ?? _settings.DatabasePath);
        _workspace = new WorkspaceStore(WorkspacePath());
        _multi = new MultiSourceDataAccess(_workspace);

        Dashboard = new DashboardViewModel(_data);
        History = new HistoryViewModel(_data, () => _settings.ExportDirectory)
        {
            AnnotationsProvider = () => _workspace.GetAnnotationLookup()
        };
        Annotations = new AnnotationsViewModel(_workspace);
        Fleet = new FleetViewModel(_multi)
        {
            AnnotationsProvider = () => _workspace.GetAnnotationLookup(),
            OnViewServer = ViewServer,
            PolicyFolderProvider = () => _settings.FirewallPolicyFolder
        };
        Remote = new RemoteViewModel(_multi, () =>
        {
            Fleet.ReloadMachines();
            Fleet.RefreshCommand.Execute(null);
            RefreshMachineOptions();
            Firewall.RefreshMachines();
        });
        Map = new MapViewModel(_data, _multi);
        Firewall = new FirewallViewModel(_multi, () => _settings.DatabasePath,
            _settings.FirewallPolicyFolder,
            folder => { _settings.FirewallPolicyFolder = folder; _settings.Save(); });
        Settings = new SettingsViewModel(_settings, OnSettingsChanged);

        // Console mode: no local collector (or forced). Hide This-Machine tabs.
        var localPresent = _data.DatabaseExists || WindowsServiceControl.IsInstalled();
        ThisMachineAvailable = localPresent && !AppModes.ForceConsole;
        if (!ThisMachineAvailable)
        {
            // Dashboard(0) History(1) Map(2) then Remote(3) Fleet(4)…; open on Fleet.
            SelectedTabIndex = 4;
            ModeBanner = AppModes.ForceConsole
                ? "Console mode (--console): viewer only. Import databases or run a remote scan from Fleet."
                : "Console mode: no local collector on this machine. Import databases or run a remote scan from Fleet.";
        }

        RefreshMachineOptions();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.RefreshIntervalSeconds))
        };
        _timer.Tick += (_, _) => { if (IsLocalActive) Dashboard.Refresh(); };
        if (ThisMachineAvailable) _timer.Start();
        _ready = true;
        if (ThisMachineAvailable) Dashboard.Refresh();
    }

    /// <summary>Rebuild the machine dropdown from the local entry plus every fleet machine.</summary>
    public void RefreshMachineOptions()
    {
        var previous = SelectedMachineOption?.Name;
        MachineOptions.Clear();
        if (ThisMachineAvailable)
            MachineOptions.Add(new MachineOption("Local (this machine)", null));
        foreach (var m in _multi.GetMachines())
            MachineOptions.Add(new MachineOption(m.Name, m.DatabasePath));

        var restore = MachineOptions.FirstOrDefault(o => o.Name == previous)
                      ?? MachineOptions.FirstOrDefault();
        SelectedMachineOption = restore;
    }

    partial void OnSelectedMachineOptionChanged(MachineOption? value)
    {
        if (!_ready && value is null) return;
        _activeDbPath = value?.DbPath;
        IsRemoteActive = value is { IsLocal: false };
        IsLocalActive = !IsRemoteActive;
        ActiveMachineBanner = IsRemoteActive
            ? $"Viewing {value!.Name} — remote/imported snapshot. Service controls are disabled; switch to Local for the live machine."
            : string.Empty;
        RefreshActiveViews();
    }

    /// <summary>Point the single-database tabs at whichever machine is active.</summary>
    private void RefreshActiveViews()
    {
        Dashboard.Refresh();
        History.RunQueryCommand.Execute(null);
        Map.RefreshCommand.Execute(null);
    }

    /// <summary>Fleet "View this server" — switch the active machine to that server.</summary>
    private void ViewServer(MachineRef machine)
    {
        RefreshMachineOptions();
        var option = MachineOptions.FirstOrDefault(o => o.Name == machine.Name);
        if (option is not null) SelectedMachineOption = option;
    }

    /// <summary>
    /// Migration dossier for the ACTIVE machine (local or the selected fleet
    /// snapshot): zip of Excel workbook + CSVs + firewall PDF + manifest.
    /// </summary>
    [RelayCommand]
    private async Task ExportDossier()
    {
        if (_dialogs is null) return;
        var name = SelectedMachineOption is { IsLocal: false } opt
            ? opt.Name
            : _data.GetMachineName() ?? Environment.MachineName;
        var dbPath = _activeDbPath ?? _settings.DatabasePath;
        var wave = _multi.GetMachines()
            .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))?.Wave ?? string.Empty;

        var suggested = $"{name}-dossier-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        var path = await _dialogs.SaveAsync(suggested, "zip", _settings.ExportDirectory);
        if (path is null) return;
        if (IsExporting) return;

        var annotations = _workspace.GetAnnotationLookup();
        var policyFolder = _settings.FirewallPolicyFolder;
        var hours = Math.Max(1, History.HoursBack);
        var progress = new Progress<(int Percent, string Stage)>(p =>
        {
            ExportProgress = p.Percent;
            ExportStage = p.Stage;
        });

        // Config scan only for the LOCAL machine (reads the real filesystem).
        var isLocal = SelectedMachineOption is not { IsLocal: false };
        var keepRaw = _settings.ConfigScanKeepRaw;
        Func<IReadOnlyList<Core.Models.ServiceRecord>, List<Core.Analysis.ConfigEndpoint>>? configScan =
            isLocal && OperatingSystem.IsWindows()
                ? svcs => ConfigScanHelper.ScanLocal(svcs, keepRaw)
                : null;

        // Most recent saved baseline for this machine, if any.
        (string, DateTime, IReadOnlyList<Core.Models.ConnectionAggregate>)? baseline = null;
        var bl = _workspace.GetBaselines().FirstOrDefault(b =>
            string.Equals(b.Machine, name, StringComparison.OrdinalIgnoreCase));
        if (bl is not null)
            baseline = (bl.Name, bl.Created, _workspace.GetBaselineFlows(bl.Id));

        IsExporting = true;
        ExportProgress = 0;
        ExportStage = "Starting…";
        try
        {
            await Task.Run(() => DossierExporter.Export(
                path, name, dbPath, wave, hours, _multi,
                annotations, policyFolder, FindLogo(), progress, baseline, configScan));
            ShellHelper.RevealAfterExport(path);
            History.ResultSummary = $"Dossier exported: {path}";
        }
        catch (Exception ex)
        {
            History.ResultSummary = "Dossier export failed: " + ex.Message;
        }
        finally
        {
            IsExporting = false;
            ExportStage = string.Empty;
        }
    }

    private static string? FindLogo()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "assets", "DependenSee.png");
        return File.Exists(p) ? p : null;
    }

    /// <summary>
    /// Save the active machine's current distinct flows as a named baseline, so
    /// a later dossier can diff against it (pre/post-cutover validation).
    /// </summary>
    [RelayCommand]
    private void SaveBaseline()
    {
        var name = SelectedMachineOption is { IsLocal: false } opt
            ? opt.Name : _data.GetMachineName() ?? Environment.MachineName;
        var flows = _data.QueryUnique(new ConnectionQuery { Limit = 1_000_000 });
        if (flows.Count == 0) { History.ResultSummary = "No flows to baseline yet."; return; }
        var label = $"{name} {DateTime.Now:yyyy-MM-dd HH:mm}";
        _workspace.SaveBaseline(label, name, flows);
        History.ResultSummary = $"Baseline saved: {label} ({flows.Count} flows). " +
            "The next dossier for this machine will include a diff against it.";
    }

    /// <summary>Called once the window exists so dialogs (save/open) are available.</summary>
    public void AttachDialogs(IDialogService dialogs)
    {
        _dialogs = dialogs;
        History.SaveService = dialogs;
        Fleet.OpenService = dialogs;
        Fleet.SaveService = dialogs;
        Firewall.SaveService = dialogs;
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
