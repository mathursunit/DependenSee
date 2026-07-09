using ServiceMap.Core.Models;

namespace ServiceMap.Platform.Abstractions;

/// <summary>
/// Optional event-driven capture of connection activity between polling sweeps.
/// A polling sampler only sees sockets alive at the instant of the sweep, so
/// short-lived flows (DNS lookups, quick API calls, SQL logins) are invisible
/// to it — yet those are exactly what firewall rules must allow. An event
/// watcher (ETW on Windows) records connection establishment as it happens and
/// hands the accumulated flows to the engine on each sweep via <see cref="Drain"/>.
/// </summary>
public interface IConnectionEventWatcher : IDisposable
{
    /// <summary>True once started successfully and still processing events.</summary>
    bool IsRunning { get; }

    /// <summary>Why the watcher is not running (e.g. not elevated), if known.</summary>
    string? LastError { get; }

    /// <summary>Begin capturing. Must not throw; check <see cref="IsRunning"/> after.</summary>
    void Start();

    /// <summary>
    /// Return the flows observed since the previous drain and reset the buffer.
    /// Samples carry a definitive <see cref="ConnectionSample.Direction"/> taken
    /// from the event type (connect = outbound, accept = inbound).
    /// </summary>
    IReadOnlyList<ConnectionSample> Drain();
}
