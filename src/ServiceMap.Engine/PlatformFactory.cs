using ServiceMap.Platform.Abstractions;
using ServiceMap.Platform.Windows;

namespace ServiceMap.Engine;

/// <summary>
/// Selects the platform provider for the current OS. v1 supplies the Windows
/// provider; the Linux daemon host will extend this once implemented.
/// </summary>
public static class PlatformFactory
{
    public static IPlatformProvider Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatformProvider();

        throw new PlatformNotSupportedException(
            "The collector currently supports Windows. The Linux systemd daemon " +
            "is on the roadmap; the Linux platform provider already exists in " +
            "ServiceMap.Platform.Linux and can be wired in here.");
    }
}
