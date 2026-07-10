using System.Linq;
using ServiceMap.Core.Net;
using ServiceMap.Core.Storage;
using ServiceMap.Platform.Abstractions;
using ServiceMap.Core.Models;

namespace ServiceMap.Engine;

/// <summary>
/// Orchestration of a single collection unit of work: sample sockets and/or
/// snapshot services, merge in event-captured flows, then persist. Timing is
/// owned by the caller (the collector service worker).
/// </summary>
public sealed class CollectionEngine : IDisposable
{
    private readonly IPlatformProvider _platform;
    private readonly SampleRepository _repository;
    private readonly bool _filterNoise;
    private readonly IConnectionEventWatcher? _eventWatcher;

    // Most recent PID -> owning service(s) map, refreshed on each service scan.
    private volatile IReadOnlyDictionary<int, string> _pidToService =
        new Dictionary<int, string>();

    public CollectionEngine(IPlatformProvider platform, SampleRepository repository,
                            bool filterNoise = true, bool enableEventCapture = false)
    {
        _platform = platform;
        _repository = repository;
        _filterNoise = filterNoise;
        if (enableEventCapture)
        {
            _eventWatcher = platform.CreateEventWatcher();
            _eventWatcher?.Start();
        }
    }

    private IDnsWatcher? _dnsWatcher;
    private IMetricSampler? _metricSampler;

    /// <summary>Start optional DNS capture (idempotent).</summary>
    public void EnableDnsCapture()
    {
        if (_dnsWatcher is not null) return;
        _dnsWatcher = _platform.CreateDnsWatcher();
        _dnsWatcher?.Start();
    }

    public bool DnsCaptureActive => _dnsWatcher?.IsRunning == true;
    public string? DnsCaptureError => _dnsWatcher?.LastError;

    /// <summary>Drain captured DNS resolutions into the store (call on a slow cadence).</summary>
    public int DrainDns()
    {
        if (_dnsWatcher is not { IsRunning: true }) return 0;
        var rows = _dnsWatcher.Drain();
        if (rows.Count == 0) return 0;
        _repository.UpsertDnsResolutions(rows);
        return rows.Count;
    }

    public void EnableMetrics()
    {
        _metricSampler ??= _platform.CreateMetricSampler();
    }

    public bool MetricsActive => _metricSampler?.IsSupported == true;

    /// <summary>Take one utilization sample and persist it.</summary>
    public void SampleMetricsOnce()
    {
        if (_metricSampler is not { IsSupported: true }) return;
        var (cpu, mem, iops, mbps) = _metricSampler.Sample();
        _repository.InsertMetricSamples(new[] { (DateTime.UtcNow, cpu, mem, iops, mbps) });
    }

    public string PlatformName => _platform.PlatformName;
    public bool IsElevated => _platform.IsElevated;

    /// <summary>True when event-driven (ETW) capture is active alongside polling.</summary>
    public bool EventCaptureActive => _eventWatcher?.IsRunning == true;

    /// <summary>Why event capture is not active, when it was requested but failed.</summary>
    public string? EventCaptureError => _eventWatcher?.LastError;

    /// <summary>
    /// Take one connection sweep, merge in flows captured by the event watcher
    /// since the last sweep, attribute each to its owning service, drop noise,
    /// and persist.
    /// </summary>
    public int SampleConnectionsOnce()
    {
        IReadOnlyList<Core.Models.ConnectionSample> sweep = _platform.ConnectionSampler.Sample();
        var samples = MergeEventFlows(sweep);

        var map = _pidToService;
        if (map.Count > 0)
        {
            foreach (var s in samples)
            {
                if (s.ProcessId > 0 && map.TryGetValue(s.ProcessId, out var svc))
                    s.ServiceName = svc;
            }
        }

        // Drop high-volume discovery/multicast chatter before it hits the DB.
        var toStore = _filterNoise
            ? samples.Where(s => !NoiseFilter.IsNoise(s)).ToList()
            : samples;

        _repository.InsertConnectionSamples(toStore, sweepCount: 1);
        return toStore.Count;
    }

    /// <summary>
    /// Append event-captured flows that the polling sweep did not already see
    /// (a flow alive at sweep time appears in both; the sweep row wins so
    /// state/direction stay consistent with historical behavior).
    /// </summary>
    private List<Core.Models.ConnectionSample> MergeEventFlows(
        IReadOnlyList<Core.Models.ConnectionSample> sweep)
    {
        var merged = new List<Core.Models.ConnectionSample>(sweep);
        if (_eventWatcher is not { IsRunning: true }) return merged;

        var seen = new HashSet<(Core.Models.Protocol, Core.Models.ConnectionDirection, int, string, int, string, int)>();
        foreach (var s in sweep)
            seen.Add((s.Protocol, s.Direction, s.ProcessId,
                      s.LocalAddress, s.LocalPort, s.RemoteAddress, s.RemotePort));

        foreach (var e in _eventWatcher.Drain())
        {
            if (seen.Contains((e.Protocol, e.Direction, e.ProcessId,
                               e.LocalAddress, e.LocalPort, e.RemoteAddress, e.RemotePort)))
                continue;
            merged.Add(e);
        }
        return merged;
    }

    public void Dispose()
    {
        _eventWatcher?.Dispose();
        _dnsWatcher?.Dispose();
        _metricSampler?.Dispose();
    }

    /// <summary>Snapshot the registered services, refresh the PID map, and persist.</summary>
    public int ScanServicesOnce()
    {
        var services = _platform.ServiceEnumerator.GetServices();

        // Build PID -> service display name(s). A PID (e.g. svchost) can host
        // several services, so names are joined.
        var map = new Dictionary<int, string>();
        foreach (var svc in services)
        {
            if (svc.ProcessId <= 0) continue;
            var label = string.IsNullOrWhiteSpace(svc.DisplayName) ? svc.Name : svc.DisplayName;
            map[svc.ProcessId] = map.TryGetValue(svc.ProcessId, out var existing)
                ? existing + ", " + label
                : label;
        }
        _pidToService = map;

        _repository.InsertServiceSnapshot(services);
        return services.Count;
    }
}
