using ServiceMap.Remote.Models;

namespace ServiceMap.Remote;

/// <summary>Collects services and connection samples from one remote host.</summary>
public interface IRemoteCollector
{
    /// <summary>The OS this collector handles.</summary>
    OsKind Handles { get; }

    Task<RemoteResult> CollectAsync(RemoteTarget target, CancellationToken ct);
}
