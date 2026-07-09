using System.Collections.Concurrent;
using ServiceMap.Core.Storage;
using ServiceMap.Remote.Collectors;
using ServiceMap.Remote.Models;

namespace ServiceMap.Remote;

/// <summary>Progress for one host as a fleet scan runs.</summary>
public sealed class ScanProgress
{
    public int Index { get; init; }
    public int Total { get; init; }
    public string Host { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Runs remote collection across many hosts with bounded concurrency and writes
/// each successful host into its own SQLite database (one file per machine),
/// which the Fleet view can then load with the full toolset.
/// </summary>
public sealed class RemoteScanService
{
    private readonly IReadOnlyList<IRemoteCollector> _collectors;
    private readonly string _outputDir;

    /// <summary>Raw per-sweep samples older than this are pruned after each store.</summary>
    public int RawRetentionDays { get; set; } = 7;

    /// <summary>Aggregated flows older than this are pruned after each store.</summary>
    public int FlowRetentionDays { get; set; } = 30;

    public RemoteScanService(string outputDir, IEnumerable<IRemoteCollector>? collectors = null)
    {
        _outputDir = outputDir;
        _collectors = collectors?.ToList() ?? new List<IRemoteCollector>
        {
            new WinRmRemoteCollector(),
            new SshRemoteCollector()
        };
    }

    public async Task<IReadOnlyList<RemoteResult>> ScanAsync(
        IReadOnlyList<RemoteTarget> targets, int maxParallel,
        IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(_outputDir);
        var results = new ConcurrentBag<RemoteResult>();
        var done = 0;
        var total = targets.Count;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, maxParallel),
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(targets, options, async (target, token) =>
        {
            var res = await CollectOne(target, token).ConfigureAwait(false);
            if (res.Success)
            {
                try { Store(res); }
                catch (Exception ex) { res.Error = "collected but not stored: " + ex.Message; }
            }
            results.Add(res);

            var n = Interlocked.Increment(ref done);
            progress?.Report(new ScanProgress
            {
                Index = n,
                Total = total,
                Host = target.Host,
                MachineName = res.MachineName,
                Success = res.Success,
                Message = res.Success
                    ? (res.Connections.Count == 0 && res.Services.Count == 0
                        ? $"connected but returned no data — check ss/systemctl are present and try a root/sudo account ({res.DurationMs} ms)"
                        : $"{res.Connections.Count} sockets, {res.Services.Count} services ({res.DurationMs} ms)")
                    : res.Error ?? "failed"
            });
        }).ConfigureAwait(false);

        return results.OrderBy(r => r.Host).ToList();
    }

    private async Task<RemoteResult> CollectOne(RemoteTarget target, CancellationToken ct)
    {
        if (target.Os == OsKind.Windows) return await Collector(OsKind.Windows).CollectAsync(target, ct);
        if (target.Os == OsKind.Linux) return await Collector(OsKind.Linux).CollectAsync(target, ct);

        // Auto: try WinRM, then SSH.
        var win = await Collector(OsKind.Windows).CollectAsync(WithOs(target, OsKind.Windows), ct);
        if (win.Success) return win;
        var lin = await Collector(OsKind.Linux).CollectAsync(WithOs(target, OsKind.Linux), ct);
        if (lin.Success) return lin;

        win.Error = $"WinRM: {win.Error}  |  SSH: {lin.Error}";
        return win;
    }

    private IRemoteCollector Collector(OsKind os) =>
        _collectors.First(c => c.Handles == os);

    private static RemoteTarget WithOs(RemoteTarget t, OsKind os)
    {
        var c = t.Clone();
        c.Os = os;
        return c;
    }

    private void Store(RemoteResult res)
    {
        var label = res.MachineName.Length > 0 ? res.MachineName : res.Host;
        var dbPath = Path.Combine(_outputDir, Sanitize(label) + ".db");
        using var repo = new SampleRepository(dbPath, readOnly: false);
        repo.Initialize();
        repo.SetMeta("machine_name", label);
        repo.SetMeta("collection_source", "remote-scan");
        repo.InsertServiceSnapshot(res.Services);
        repo.InsertConnectionSamples(res.Connections, res.SweepCount);

        // Scheduled scans append forever; apply the same retention split the
        // local collector uses so per-host databases stay bounded.
        var now = DateTime.UtcNow;
        var rawDays = Math.Min(Math.Max(1, RawRetentionDays), FlowRetentionDays);
        repo.PruneConnectionsOlderThan(now.AddDays(-rawDays), now.AddDays(-FlowRetentionDays));
        res.StoredPath = dbPath;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(':', '_');
    }
}
