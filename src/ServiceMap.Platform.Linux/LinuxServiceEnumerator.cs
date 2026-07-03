using System.Diagnostics;
using ServiceMap.Core.Models;
using ServiceMap.Platform.Abstractions;

namespace ServiceMap.Platform.Linux;

/// <summary>
/// Enumerates systemd service units by invoking <c>systemctl</c>. Partial port
/// target: returns unit name, description, and active/sub state. PID and exec
/// path enrichment via <c>systemctl show</c> is left as a follow-up.
/// </summary>
public sealed class LinuxServiceEnumerator : IServiceEnumerator
{
    public IReadOnlyList<ServiceRecord> GetServices()
    {
        var now = DateTime.UtcNow;
        var list = new List<ServiceRecord>();

        string output;
        try
        {
            output = RunSystemctl(
                "list-units --type=service --all --no-legend --no-pager --plain");
        }
        catch
        {
            // systemd not present (e.g. container); return empty rather than throw.
            return list;
        }

        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 4) continue;

            // Columns: UNIT LOAD ACTIVE SUB DESCRIPTION...
            string unit = cols[0];
            string active = cols[2];
            string sub = cols[3];
            string description = cols.Length > 4 ? string.Join(' ', cols[4..]) : unit;

            list.Add(new ServiceRecord
            {
                Name = unit,
                DisplayName = description,
                State = $"{active}/{sub}",
                StartMode = string.Empty, // requires `systemctl is-enabled`; deferred.
                ProcessId = 0,
                ExecutablePath = null,
                Account = null,
                ScanTimestamp = now
            });
        }

        return list;
    }

    private static string RunSystemctl(string args)
    {
        var psi = new ProcessStartInfo("systemctl", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return stdout;
    }
}
