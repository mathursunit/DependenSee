using ServiceMap.Core.Models;

namespace ServiceMap.Platform.Abstractions;

/// <summary>Enumerates the OS-registered services on this host.</summary>
public interface IServiceEnumerator
{
    /// <summary>Snapshot the current set of registered services.</summary>
    IReadOnlyList<ServiceRecord> GetServices();
}
