using System.Runtime.Versioning;
using System.Security.Principal;
using ServiceMap.Platform.Abstractions;

namespace ServiceMap.Platform.Windows;

/// <summary>Windows implementation of the platform provider.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformProvider : IPlatformProvider
{
    public string PlatformName => "Windows";

    public IServiceEnumerator ServiceEnumerator { get; } = new WindowsServiceEnumerator();
    public IConnectionSampler ConnectionSampler { get; } = new WindowsConnectionSampler();

    /// <summary>ETW kernel-network capture for flows too short-lived for polling.</summary>
    public IConnectionEventWatcher? CreateEventWatcher() => new EtwConnectionWatcher();

    /// <summary>ETW DNS-Client capture (name resolutions per process).</summary>
    public IDnsWatcher? CreateDnsWatcher() => new EtwDnsWatcher();

    /// <summary>Windows performance-counter utilization sampler.</summary>
    public IMetricSampler? CreateMetricSampler() => new WindowsMetricSampler();

    public bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
