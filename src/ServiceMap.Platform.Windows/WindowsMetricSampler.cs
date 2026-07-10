using System.Diagnostics;
using System.Runtime.Versioning;
using ServiceMap.Platform.Abstractions;

namespace ServiceMap.Platform.Windows;

/// <summary>
/// Samples machine-wide resource utilization via Windows performance counters.
/// Peak/percentile rollups of these feed target instance-type sizing. Counters
/// are created lazily and failures degrade to zero for that metric rather than
/// throwing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMetricSampler : IMetricSampler
{
    private PerformanceCounter? _cpu;
    private PerformanceCounter? _availMem;
    private PerformanceCounter? _diskTransfers;
    private PerformanceCounter? _netBytes;
    private readonly double _totalMemMb;
    private bool _primed;

    public bool IsSupported => OperatingSystem.IsWindows();

    public WindowsMetricSampler()
    {
        try { _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); } catch { }
        try { _availMem = new PerformanceCounter("Memory", "Available MBytes"); } catch { }
        try { _diskTransfers = new PerformanceCounter("PhysicalDisk", "Disk Transfers/sec", "_Total"); } catch { }
        try
        {
            var cat = new PerformanceCounterCategory("Network Interface");
            var inst = cat.GetInstanceNames().FirstOrDefault(n =>
                !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase) &&
                !n.Contains("isatap", StringComparison.OrdinalIgnoreCase));
            if (inst is not null) _netBytes = new PerformanceCounter("Network Interface", "Bytes Total/sec", inst);
        }
        catch { }
        _totalMemMb = TotalPhysicalMemoryMb();
    }

    public (double CpuPercent, double MemUsedMb, double DiskIops, double NetMbps) Sample()
    {
        // First read of a rate counter is always 0; prime once.
        if (!_primed) { _ = Read(_cpu); _ = Read(_diskTransfers); _ = Read(_netBytes); _primed = true; }
        var cpu = Read(_cpu);
        var avail = Read(_availMem);
        var memUsed = _totalMemMb > 0 ? Math.Max(0, _totalMemMb - avail) : 0;
        var iops = Read(_diskTransfers);
        var mbps = Read(_netBytes) * 8 / 1_000_000.0;   // bytes/sec -> Mbps
        return (cpu, memUsed, iops, mbps);
    }

    private static double Read(PerformanceCounter? c)
    {
        try { return c?.NextValue() ?? 0; } catch { return 0; }
    }

    private static double TotalPhysicalMemoryMb()
    {
        try
        {
            using var mc = new System.Management.ManagementClass("Win32_ComputerSystem");
            foreach (System.Management.ManagementObject mo in mc.GetInstances())
                if (mo["TotalPhysicalMemory"] is { } v && ulong.TryParse(v.ToString(), out var bytes))
                    return bytes / 1024.0 / 1024.0;
        }
        catch { }
        return 0;
    }

    public void Dispose()
    {
        _cpu?.Dispose(); _availMem?.Dispose(); _diskTransfers?.Dispose(); _netBytes?.Dispose();
    }
}
