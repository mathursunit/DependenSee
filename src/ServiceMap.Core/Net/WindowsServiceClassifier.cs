using ServiceMap.Core.Models;

namespace ServiceMap.Core.Net;

/// <summary>
/// Heuristic to tell built-in Windows services from third-party/app services,
/// so the dashboard can hide the OS noise. A service is treated as "standard"
/// when its executable lives under the Windows directory (or it has no path but
/// a well-known system name).
/// </summary>
public static class WindowsServiceClassifier
{
    public static bool IsStandard(ServiceRecord svc)
    {
        var path = svc.ExecutablePath ?? string.Empty;
        // Strip a leading quote and any command-line arguments.
        path = path.Trim().Trim('"');

        if (path.Length > 0)
        {
            var lower = path.ToLowerInvariant();
            if (lower.Contains(@"\windows\") || lower.Contains("/windows/"))
                return true;
            // Anything with a real path outside Windows is considered third-party.
            if (lower.Contains(@"\program files") || lower.Contains(@":\") || lower.Contains('/'))
                return false;
        }

        // No usable path: fall back to a small set of well-known system names.
        return KnownSystemNames.Contains(svc.Name);
    }

    private static readonly HashSet<string> KnownSystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dnscache", "Dhcp", "Netlogon", "LanmanServer", "LanmanWorkstation", "Spooler",
        "Schedule", "Themes", "EventLog", "W32Time", "WinRM", "TermService", "RpcSs",
        "BITS", "wuauserv", "WSearch", "Winmgmt", "PlugPlay", "Power", "ProfSvc",
        "gpsvc", "CryptSvc", "TrustedInstaller", "MSDTC", "WlanSvc", "Audiosrv"
    };
}
