namespace ServiceMap.Platform.Abstractions;

/// <summary>
/// Bundles the platform-specific collectors. The Collector and GUI depend only
/// on this abstraction; the Windows or Linux assembly supplies the concrete
/// implementation selected at startup.
/// </summary>
public interface IPlatformProvider
{
    /// <summary>Human-readable platform name, e.g. "Windows".</summary>
    string PlatformName { get; }

    /// <summary>True when the current process has the privileges needed for full
    /// port-to-process attribution and complete service details.</summary>
    bool IsElevated { get; }

    IServiceEnumerator ServiceEnumerator { get; }
    IConnectionSampler ConnectionSampler { get; }

    /// <summary>
    /// Create an event-driven connection watcher for platforms that support one
    /// (ETW on Windows), or null when only polling is available. Default: null,
    /// so platform implementations without event capture need no change.
    /// </summary>
    IConnectionEventWatcher? CreateEventWatcher() => null;
}
