using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.App.Services;
using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;
using ServiceMap.Reporting;

namespace ServiceMap.App.ViewModels;

/// <summary>
/// Fleet view: import several machines' collector databases and see unioned
/// dependencies and machine-to-machine links across the group. Selecting a
/// machine narrows the flow list to that machine; each machine can be exported
/// as a firewall-rule PDF.
/// </summary>
public sealed partial class FleetViewModel : ViewModelBase
{
    private readonly MultiSourceDataAccess _multi;

    // Full, unfiltered result of the last Refresh; the visible collections are
    // derived from these when a machine is selected.
    private readonly List<ConnectionAggregate> _allFlows = new();
    private readonly List<CrossDependency> _allDeps = new();

    /// <summary>Native open dialog; set once the window exists.</summary>
    public IFileOpenService? OpenService { get; set; }

    /// <summary>Native save dialog for the per-server PDF export.</summary>
    public IFileSaveService? SaveService { get; set; }

    /// <summary>Annotations lookup used to enrich the firewall report.</summary>
    public Func<IReadOnlyDictionary<string, Annotation>>? AnnotationsProvider { get; set; }

    /// <summary>Callback to open the selected server in the main tabs (set by the root VM).</summary>
    public Action<MachineRef>? OnViewServer { get; set; }

    /// <summary>Where firewall CSV exports live (from Settings); used by the dossier export.</summary>
    public Func<string>? PolicyFolderProvider { get; set; }

    public ObservableCollection<MachineRef> Machines { get; } = new();
    public ObservableCollection<ConnectionAggregate> Flows { get; } = new();
    public ObservableCollection<CrossDependency> CrossDependencies { get; } = new();

    [ObservableProperty] private MachineRef? _selectedMachine;
    [ObservableProperty] private string? _waveInput;
    [ObservableProperty] private int _hoursBack = 168;
    [ObservableProperty] private string _status = "Import or scan one or more machines to begin.";

    public FleetViewModel(MultiSourceDataAccess multi)
    {
        _multi = multi;
        ReloadMachines();
        // Scheduled scans may have written new per-host databases while the
        // GUI was closed; pick them up without requiring a manual import.
        try { SyncRemoteScans(); } catch { /* best effort at startup */ }
    }

    /// <summary>Where RemoteScanService / the scheduled CLI write per-host databases.</summary>
    private static string RemoteScanDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CarrierDependenSee", "remote");

    /// <summary>
    /// Import any remote-scan databases not yet registered in the workspace.
    /// Safe to run repeatedly: imports are idempotent by database path.
    /// </summary>
    [RelayCommand]
    private void SyncRemoteScans()
    {
        if (!Directory.Exists(RemoteScanDir)) return;

        var known = new HashSet<string>(
            _multi.GetMachines().Select(m => Path.GetFullPath(m.DatabasePath)),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var db in Directory.EnumerateFiles(RemoteScanDir, "*.db"))
        {
            if (known.Contains(Path.GetFullPath(db))) continue;
            try { _multi.ImportMachine(db); added++; }
            catch { /* skip unreadable db */ }
        }

        if (added > 0)
        {
            ReloadMachines();
            Refresh();
            Status = $"Imported {added} new remote-scan database(s).";
        }
    }

    partial void OnSelectedMachineChanged(MachineRef? value)
    {
        WaveInput = value?.Wave;
        // Load data on first selection, otherwise just re-filter the cached set.
        if (value is not null && _allFlows.Count == 0) Refresh();
        else ApplyMachineFilter();
    }

    public void ReloadMachines()
    {
        Machines.Clear();
        foreach (var m in _multi.GetMachines()) Machines.Add(m);
    }

    [RelayCommand]
    private async Task ImportMachine()
    {
        if (OpenService is null) return;
        var path = await OpenService.OpenAsync("Import collector database (servicemap.db)", "db");
        if (path is null) return;
        var m = _multi.ImportMachine(path);
        ReloadMachines();
        Status = $"Imported {m.Name}.";
        Refresh();
    }

    [RelayCommand]
    private void RemoveMachine()
    {
        if (SelectedMachine is null) { Status = "Select a machine to remove."; return; }
        _multi.RemoveMachine(SelectedMachine.Id);
        SelectedMachine = null;
        ReloadMachines();
        Refresh();
        Status = "Machine removed.";
    }

    [RelayCommand]
    private void AssignWave()
    {
        if (SelectedMachine is null) { Status = "Select a machine first."; return; }
        _multi.SetWave(SelectedMachine.Id, WaveInput ?? "");
        ReloadMachines();
        Status = $"Wave set to \"{WaveInput}\".";
    }

    [RelayCommand]
    private void Refresh()
    {
        var q = BuildQuery();

        _allFlows.Clear();
        _allFlows.AddRange(_multi.QueryUniqueAll(q)
            .OrderBy(f => f.Machine).ThenByDescending(f => f.SampleCount));

        _allDeps.Clear();
        _allDeps.AddRange(_multi.DetectCrossDependencies(q)
            .OrderBy(d => d.FromMachine).ThenBy(d => d.ToMachine));

        ApplyMachineFilter();
    }

    /// <summary>Narrow the visible flows/dependencies to the selected machine (or show all).</summary>
    private void ApplyMachineFilter()
    {
        var name = SelectedMachine?.Name;

        var flows = string.IsNullOrEmpty(name)
            ? _allFlows
            : _allFlows.Where(f => string.Equals(f.Machine, name, StringComparison.OrdinalIgnoreCase)).ToList();
        Flows.Clear();
        foreach (var f in flows) Flows.Add(f);

        var deps = string.IsNullOrEmpty(name)
            ? _allDeps
            : _allDeps.Where(d =>
                string.Equals(d.FromMachine, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.ToMachine, name, StringComparison.OrdinalIgnoreCase)).ToList();
        CrossDependencies.Clear();
        foreach (var d in deps) CrossDependencies.Add(d);

        if (Machines.Count == 0)
            Status = "Import or scan one or more machines to begin.";
        else if (string.IsNullOrEmpty(name))
            Status = $"{Machines.Count} machines · {_allFlows.Count} flows · {_allDeps.Count} machine-to-machine dependencies (all machines).";
        else
            Status = $"{name}: {flows.Count} flows · {deps.Count} dependencies involving this machine.";
    }

    [RelayCommand]
    private async Task ExportServerPdf()
    {
        if (SelectedMachine is null) { Status = "Select a machine to export."; return; }
        if (SaveService is null) return;

        var machine = SelectedMachine;
        var suggested = $"{machine.Name}-firewall-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";
        var initialDir = Path.GetDirectoryName(machine.DatabasePath);
        var path = await SaveService.SaveAsync(suggested, "pdf", initialDir);
        if (path is null) return;

        var data = new DataAccess(() => machine.DatabasePath);
        var options = new FirewallReportOptions
        {
            ResolveHostnames = true,
            SummarizeSourcesToCidr = false,
            LogoPath = FindLogo(),
            FilterSummary = $"{machine.Name} · last {Math.Max(1, HoursBack)}h · IPv4",
            Annotations = AnnotationsProvider?.Invoke()
        };
        var report = data.BuildFirewallReport(BuildQuery(), options);
        FirewallReportPdf.Save(report, path);
        ShellHelper.RevealAfterExport(path);
        Status = $"Exported {machine.Name}: {report.Inbound.Count} inbound, {report.Outbound.Count} outbound rules → {path}";
    }

    /// <summary>
    /// One-click migration dossier for the selected server: zip containing the
    /// Excel workbook, per-section CSVs, the firewall PDF, and a manifest.
    /// </summary>
    [RelayCommand]
    private async Task ExportDossier()
    {
        if (SelectedMachine is null) { Status = "Select a machine to export."; return; }
        if (SaveService is null) return;

        var machine = SelectedMachine;
        var suggested = $"{machine.Name}-dossier-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        var path = await SaveService.SaveAsync(suggested, "zip", Path.GetDirectoryName(machine.DatabasePath));
        if (path is null) return;

        try
        {
            Status = $"Building dossier for {machine.Name}…";
            DossierExporter.Export(
                path, machine.Name, machine.DatabasePath, machine.Wave ?? string.Empty,
                Math.Max(1, HoursBack), _multi,
                AnnotationsProvider?.Invoke() ?? new Dictionary<string, Annotation>(),
                PolicyFolderProvider?.Invoke() ?? string.Empty,
                FindLogo());
            ShellHelper.RevealAfterExport(path);
            Status = $"Dossier exported: {path}";
        }
        catch (Exception ex)
        {
            Status = "Dossier export failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private void ViewServer()
    {
        if (SelectedMachine is null) { Status = "Select a machine to view."; return; }
        OnViewServer?.Invoke(SelectedMachine);
    }

    private ConnectionQuery BuildQuery() => new()
    {
        From = DateTime.UtcNow.AddHours(-Math.Max(1, HoursBack)),
        AddressFamily = AddressFamilyOption.IPv4,
        Limit = 1_000_000
    };

    private static string? FindLogo()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "assets", "DependenSee.png");
        return File.Exists(p) ? p : null;
    }
}
