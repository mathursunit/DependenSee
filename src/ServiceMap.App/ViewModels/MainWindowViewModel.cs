using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.App.Services;
using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;

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

        RefreshMachineOptions();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.RefreshIntervalSeconds))
        };
        _timer.Tick += (_, _) => Dashboard.Refresh();
        _timer.Start();
        _ready = true;
        Dashboard.Refresh();
    }

    /// <summary>Rebuild the machine dropdown from the local entry plus every fleet machine.</summary>
    public void RefreshMachineOptions()
    {
        var previous = SelectedMachineOption?.Name;
        MachineOptions.Clear();
        MachineOptions.Add(new MachineOption("Local (this machine)", null));
        foreach (var m in _multi.GetMachines())
            MachineOptions.Add(new MachineOption(m.Name, m.DatabasePath));

        var restore = MachineOptions.FirstOrDefault(o => o.Name == previous) ?? MachineOptions[0];
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

        try
        {
            History.ResultSummary = $"Building dossier for {name}…";
            DossierExporter.Export(
                path, name, dbPath, wave, Math.Max(1, History.HoursBack), _multi,
                _workspace.GetAnnotationLookup(), _settings.FirewallPolicyFolder, FindLogo());
            ShellHelper.RevealAfterExport(path);
            History.ResultSummary = $"Dossier exported: {path}";
        }
        catch (Exception ex)
        {
            History.ResultSummary = "Dossier export failed: " + ex.Message;
        }
    }

    private static string? FindLogo()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "assets", "DependenSee.png");
        return File.Exists(p) ? p : null;
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
