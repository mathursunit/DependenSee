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
