using ServiceMap.Core.Models;

namespace ServiceMap.Platform.Abstractions;

/// <summary>
/// Samples the host's current sockets: listening endpoints and active
/// connections, attributed to the owning process where possible.
/// </summary>
public interface IConnectionSampler
{
    /// <summary>
    /// Take one snapshot of all TCP/UDP endpoints. All returned samples should
    /// share the same <see cref="ConnectionSample.Timestamp"/> so a sweep can be
    /// treated as an atomic batch.
    /// </summary>
    IReadOnlyList<ConnectionSample> Sample();
}
