using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.App.Services;
using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;

namespace ServiceMap.App.ViewModels;

/// <summary>
/// Fleet view: import several machines' collector databases and see unioned
/// dependencies and machine-to-machine links across the group.
/// </summary>
public sealed partial class FleetViewModel : ViewModelBase
{
    private readonly MultiSourceDataAccess _multi;

    /// <summary>Native open dialog; set once the window exists.</summary>
    public IFileOpenService? OpenService { get; set; }

    public ObservableCollection<MachineRef> Machines { get; } = new();
    public ObservableCollection<ConnectionAggregate> Flows { get; } = new();
    public ObservableCollection<CrossDependency> CrossDependencies { get; } = new();

    [ObservableProperty] private MachineRef? _selectedMachine;
    [ObservableProperty] private string? _waveInput;
    [ObservableProperty] private int _hoursBack = 168;
    [ObservableProperty] private string _status = "Import one or more collector databases to begin.";

    public FleetViewModel(MultiSourceDataAccess multi)
    {
        _multi = multi;
        ReloadMachines();
    }

    partial void OnSelectedMachineChanged(MachineRef? value) => WaveInput = value?.Wave;

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
        var q = new ConnectionQuery
        {
            From = DateTime.UtcNow.AddHours(-Math.Max(1, HoursBack)),
            AddressFamily = AddressFamilyOption.IPv4,
            Limit = 1_000_000
        };

        var flows = _multi.QueryUniqueAll(q);
        Flows.Clear();
        foreach (var f in flows.OrderBy(f => f.Machine).ThenByDescending(f => f.SampleCount))
            Flows.Add(f);

        var deps = _multi.DetectCrossDependencies(q);
        CrossDependencies.Clear();
        foreach (var d in deps.OrderBy(d => d.FromMachine).ThenBy(d => d.ToMachine))
            CrossDependencies.Add(d);

        var boundary = deps.Count(d => d.CrossesWaveBoundary
            && !string.IsNullOrEmpty(d.FromWave) && !string.IsNullOrEmpty(d.ToWave));
        Status = Machines.Count == 0
            ? "Import one or more collector databases to begin."
            : $"{Machines.Count} machines · {flows.Count} flows · {deps.Count} machine-to-machine dependencies · {boundary} cross a wave boundary.";
    }
}
