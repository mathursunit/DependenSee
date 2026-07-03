using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;

namespace ServiceMap.App.Services;

/// <summary>
/// Reads across several imported collector databases (the fleet), unioning
/// distinct flows tagged by machine and detecting machine-to-machine dependencies.
/// </summary>
public sealed class MultiSourceDataAccess
{
    private readonly WorkspaceStore _workspace;

    public MultiSourceDataAccess(WorkspaceStore workspace) => _workspace = workspace;

    public IReadOnlyList<MachineRef> GetMachines() => _workspace.GetMachines();

    /// <summary>Union of distinct flows across all imported machines, tagged by machine.</summary>
    public IReadOnlyList<ConnectionAggregate> QueryUniqueAll(ConnectionQuery q)
    {
        var result = new List<ConnectionAggregate>();
        foreach (var m in _workspace.GetMachines())
        {
            if (!File.Exists(m.DatabasePath)) continue;
            try
            {
                using var repo = new SampleRepository(m.DatabasePath, readOnly: true);
                foreach (var f in repo.QueryUniqueConnections(q))
                {
                    f.Machine = m.Name;
                    result.Add(f);
                }
            }
            catch { /* skip an unreadable machine db */ }
        }
        return result;
    }

    /// <summary>Map every imported machine's own IPs to its name.</summary>
    public Dictionary<string, string> BuildIpToMachine()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _workspace.GetMachines())
        {
            if (!File.Exists(m.DatabasePath)) continue;
            try
            {
                using var repo = new SampleRepository(m.DatabasePath, readOnly: true);
                foreach (var ip in repo.GetLocalAddresses()) map[ip] = m.Name;
            }
            catch { /* ignore */ }
        }
        return map;
    }

    /// <summary>Outbound flows whose destination is another imported machine.</summary>
    public IReadOnlyList<CrossDependency> DetectCrossDependencies(ConnectionQuery q)
    {
        var map = BuildIpToMachine();
        var waveByMachine = _workspace.GetMachines()
            .ToDictionary(m => m.Name, m => m.Wave, StringComparer.OrdinalIgnoreCase);
        string Wave(string name) => waveByMachine.TryGetValue(name, out var w) ? w : string.Empty;
        var deps = new List<CrossDependency>();
        foreach (var f in QueryUniqueAll(q))
        {
            if (f.Direction != ConnectionDirection.Outbound || string.IsNullOrEmpty(f.RemoteAddress))
                continue;
            if (map.TryGetValue(f.RemoteAddress, out var toMachine) &&
                !string.Equals(toMachine, f.Machine, StringComparison.OrdinalIgnoreCase))
            {
                deps.Add(new CrossDependency
                {
                    FromMachine = f.Machine,
                    ToMachine = toMachine,
                    FromWave = Wave(f.Machine),
                    ToWave = Wave(toMachine),
                    Process = f.ProcessName,
                    Protocol = f.Protocol,
                    RemoteAddress = f.RemoteAddress,
                    RemotePort = f.RemotePort,
                    SampleCount = f.SampleCount
                });
            }
        }
        return deps;
    }

    /// <summary>Import a collector database; the machine name comes from its meta, else the file name.</summary>
    public MachineRef ImportMachine(string dbPath)
    {
        string name;
        try
        {
            using var repo = new SampleRepository(dbPath, readOnly: true);
            name = repo.MachineName ?? Path.GetFileNameWithoutExtension(dbPath);
        }
        catch
        {
            name = Path.GetFileNameWithoutExtension(dbPath);
        }
        var id = _workspace.AddMachine(name, dbPath);
        return new MachineRef { Id = id, Name = name, DatabasePath = dbPath, Added = DateTime.UtcNow };
    }

    public void RemoveMachine(long id) => _workspace.RemoveMachine(id);
    public void SetWave(long id, string wave) => _workspace.SetWave(id, wave);
}
