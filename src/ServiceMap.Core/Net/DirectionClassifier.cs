using ServiceMap.Core.Models;

namespace ServiceMap.Core.Net;

/// <summary>
/// Remembers which local ports have recently been observed in the LISTEN state,
/// with a sliding time-to-live. This makes direction inference robust against
/// listeners that start/stop between sampling sweeps: a connection to port 8080
/// is still classified inbound for a while after the listener row itself
/// disappears from a sweep (service restart, backlog-full, race with the
/// snapshot). Pure in-memory state; safe for a single-threaded sampling loop.
/// </summary>
public sealed class ListenPortTracker
{
    private readonly TimeSpan _ttl;
    private readonly Dictionary<int, DateTime> _lastSeen = new();

    /// <param name="ttl">How long a port stays "known listening" after last observed. Default 15 minutes.</param>
    public ListenPortTracker(TimeSpan? ttl = null) => _ttl = ttl ?? TimeSpan.FromMinutes(15);

    /// <summary>Record that <paramref name="port"/> was observed listening at <paramref name="whenUtc"/>.</summary>
    public void Observe(int port, DateTime whenUtc) => _lastSeen[port] = whenUtc;

    /// <summary>True when the port was observed listening within the TTL window.</summary>
    public bool IsListening(int port, DateTime nowUtc) =>
        _lastSeen.TryGetValue(port, out var seen) && nowUtc - seen <= _ttl;

    /// <summary>Drop entries older than the TTL to bound memory over long runs.</summary>
    public void Sweep(DateTime nowUtc)
    {
        List<int>? stale = null;
        foreach (var (port, seen) in _lastSeen)
            if (nowUtc - seen > _ttl)
                (stale ??= new List<int>()).Add(port);
        if (stale is not null)
            foreach (var p in stale) _lastSeen.Remove(p);
    }

    public int Count => _lastSeen.Count;
}

/// <summary>
/// Shared direction-inference rule used by the local Windows sampler and the
/// remote scan parsers, so every code path classifies identically:
///
///   1. LISTEN sockets and all UDP endpoints are <see cref="ConnectionDirection.Listen"/>.
///   2. A connection whose local port is a known listening port is <see cref="ConnectionDirection.Inbound"/>.
///   3. Everything else is <see cref="ConnectionDirection.Outbound"/>.
///
/// The local sampler passes a <see cref="ListenPortTracker"/>-backed predicate so
/// "known listening" survives listener restarts between sweeps; single-shot
/// remote scans use the batch's own listen set via <see cref="AssignBatch"/>.
/// </summary>
public static class DirectionClassifier
{
    public static ConnectionDirection Classify(
        Protocol protocol, TcpState state, int localPort,
        Func<int, bool> isListeningPort)
    {
        if (state == TcpState.Listen || protocol == Protocol.Udp)
            return ConnectionDirection.Listen;
        if (isListeningPort(localPort))
            return ConnectionDirection.Inbound;
        return ConnectionDirection.Outbound;
    }

    /// <summary>
    /// Assign directions across a single-shot batch (remote scans): the listen
    /// set is taken from the batch itself.
    /// </summary>
    public static void AssignBatch(IEnumerable<ConnectionSample> samples)
    {
        var list = samples as ICollection<ConnectionSample> ?? samples.ToList();
        var listenPorts = list
            .Where(s => s.State == TcpState.Listen)
            .Select(s => s.LocalPort)
            .ToHashSet();

        foreach (var s in list)
            s.Direction = Classify(s.Protocol, s.State, s.LocalPort, listenPorts.Contains);
    }
}
