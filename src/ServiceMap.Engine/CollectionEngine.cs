using ServiceMap.Core.Storage;
using ServiceMap.Platform.Abstractions;

namespace ServiceMap.Engine;

/// <summary>
/// Stateless orchestration of a single collection unit of work: sample sockets
/// and/or snapshot services, then persist. Timing is owned by the caller (the
/// collector service worker), keeping this class easy to test.
/// </summary>
public sealed class CollectionEngine
{
    private readonly IPlatformProvider _platform;
    private readonly SampleRepository _repository;

    // Most recent PID -> owning service(s) map, refreshed on each service scan.
    private volatile IReadOnlyDictionary<int, string> _pidToService =
        new Dictionary<int, string>();

    public CollectionEngine(IPlatformProvider platform, SampleRepository repository)
    {
        _platform = platform;
        _repository = repository;
    }

    public string PlatformName => _platform.PlatformName;
    public bool IsElevated => _platform.IsElevated;

    /// <summary>Take one connection sweep, attribute each to its owning service, and persist.</summary>
    public int SampleConnectionsOnce()
    {
        var samples = _platform.ConnectionSampler.Sample();
        var map = _pidToService;
        if (map.Count > 0)
        {
            foreach (var s in samples)
            {
                if (s.ProcessId > 0 && map.TryGetValue(s.ProcessId, out var svc))
                    s.ServiceName = svc;
            }
        }
        _repository.InsertConnectionSamples(samples);
        return samples.Count;
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
