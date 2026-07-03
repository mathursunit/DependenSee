using System.Diagnostics;

namespace ServiceMap.Platform.Windows;

/// <summary>
/// Resolves a PID to a process name and executable path, caching results for the
/// lifetime of one sampling sweep so repeated lookups within a sweep are cheap.
/// </summary>
internal sealed class ProcessInfoCache
{
    private readonly Dictionary<int, (string Name, string? Path)> _cache = new();

    public (string Name, string? Path) Resolve(int pid)
    {
        if (pid <= 0)
            return ("System Idle/Kernel", null);

        if (_cache.TryGetValue(pid, out var cached))
            return cached;

        (string Name, string? Path) info;
        try
        {
            using var p = Process.GetProcessById(pid);
            string? path = null;
            try
            {
                // MainModule can throw for protected/system processes; tolerate it.
                path = p.MainModule?.FileName;
            }
            catch
            {
                // Access denied for a protected process; name still usable.
            }
            info = (p.ProcessName, path);
        }
        catch
        {
            // Process exited between sampling and lookup.
            info = ($"pid:{pid}", null);
        }

        _cache[pid] = info;
        return info;
    }
}
