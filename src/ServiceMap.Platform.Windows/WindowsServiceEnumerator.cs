using System.Management;
using System.Runtime.Versioning;
using ServiceMap.Core.Models;
using ServiceMap.Platform.Abstractions;

namespace ServiceMap.Platform.Windows;

/// <summary>
/// Enumerates registered Windows services via WMI (Win32_Service). WMI is used
/// rather than <c>ServiceController</c> because it also exposes the owning PID,
/// executable path, start mode, and log-on account in a single query.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceEnumerator : IServiceEnumerator
{
    public IReadOnlyList<ServiceRecord> GetServices()
    {
        var now = DateTime.UtcNow;
        var list = new List<ServiceRecord>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DisplayName, State, StartMode, ProcessId, PathName, StartName " +
            "FROM Win32_Service");

        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                list.Add(new ServiceRecord
                {
                    Name = GetString(mo, "Name"),
                    DisplayName = GetString(mo, "DisplayName"),
                    State = GetString(mo, "State"),
                    StartMode = GetString(mo, "StartMode"),
                    ProcessId = GetInt(mo, "ProcessId"),
                    ExecutablePath = GetNullableString(mo, "PathName"),
                    Account = GetNullableString(mo, "StartName"),
                    ScanTimestamp = now
                });
            }
        }

        return list;
    }

    private static string GetString(ManagementObject mo, string prop) =>
        mo[prop]?.ToString() ?? string.Empty;

    private static string? GetNullableString(ManagementObject mo, string prop)
    {
        var v = mo[prop]?.ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static int GetInt(ManagementObject mo, string prop)
    {
        var v = mo[prop];
        if (v == null) return 0;
        return Convert.ToInt32(v);
    }
}
