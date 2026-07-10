using System.Diagnostics;

namespace ServiceMap.App.Services;

/// <summary>
/// Thin wrapper for controlling the collector Windows Service from the GUI.
/// Start/stop/install/uninstall self-elevate via the UAC "runas" verb so the
/// reader itself can run unprivileged. Status is read via sc.exe to avoid a
/// Windows-only package dependency on the cross-platform GUI assembly.
/// </summary>
public static class WindowsServiceControl
{
    public const string ServiceName = "CarrierDependenSeeCollector";

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Returns a status string such as "RUNNING", "STOPPED", or "Not installed".</summary>
    /// <summary>True when the collector service is registered on this machine.</summary>
    public static bool IsInstalled()
    {
        if (!IsSupported) return false;
        try
        {
            var output = Capture("sc", $"query \"{ServiceName}\"");
            return !output.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                   && !output.Contains("1060");
        }
        catch { return false; }
    }

    public static string QueryStatus()
    {
        if (!IsSupported) return "Windows only";
        try
        {
            var output = Capture("sc", $"query \"{ServiceName}\"");
            if (output.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("1060"))
                return "Not installed";

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                {
                    // e.g. "STATE : 4 RUNNING"
                    var parts = trimmed.Split(':', 2);
                    if (parts.Length == 2)
                        return parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
                }
            }
            return "Unknown";
        }
        catch
        {
            return "Unavailable";
        }
    }

    public static void Start() => RunElevated("net", $"start \"{ServiceName}\"");
    public static void Stop() => RunElevated("net", $"stop \"{ServiceName}\"");

    public static void Install(string scriptPath) =>
        RunElevated("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");

    public static void Uninstall(string scriptPath) =>
        RunElevated("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"");

    private static string Capture(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
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

    private static void RunElevated(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = true, // required for the runas verb / UAC prompt
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        try
        {
            Process.Start(psi);
        }
        catch
        {
            // User declined the UAC prompt, or elevation failed. The next status
            // poll will reflect that nothing changed.
        }
    }
}
