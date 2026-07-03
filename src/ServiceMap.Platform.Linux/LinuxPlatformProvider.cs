using ServiceMap.Platform.Abstractions;

namespace ServiceMap.Platform.Linux;

/// <summary>Linux implementation of the platform provider (port in progress).</summary>
public sealed class LinuxPlatformProvider : IPlatformProvider
{
    public string PlatformName => "Linux";

    public IServiceEnumerator ServiceEnumerator { get; } = new LinuxServiceEnumerator();
    public IConnectionSampler ConnectionSampler { get; } = new LinuxConnectionSampler();

    /// <summary>Root (uid 0) is needed for complete socket-to-process attribution.</summary>
    public bool IsElevated
    {
        get
        {
            // geteuid() == 0. Environment.UserName is "root" under sudo/root.
            try { return Environment.UserName == "root"; }
            catch { return false; }
        }
    }
}
