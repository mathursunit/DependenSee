using ServiceMap.Core.Models;

namespace ServiceMap.Platform.Abstractions;

/// <summary>
/// Optional capture of DNS resolutions per process (ETW DNS-Client on Windows).
/// Names outlast IPs across a migration, so this feeds FQDN-based egress rules.
/// </summary>
public interface IDnsWatcher : IDisposable
{
    bool IsRunning { get; }
    string? LastError { get; }
    void Start();
    /// <summary>Distinct (process, name, IP) resolutions since the last drain.</summary>
    IReadOnlyList<DnsResolution> Drain();
}

/// <summary>Optional resource-utilization sampler for target right-sizing.</summary>
public interface IMetricSampler : IDisposable
{
    bool IsSupported { get; }
    /// <summary>Take one instantaneous sample: (cpu %, mem used MB, disk IOPS, net Mbps).</summary>
    (double CpuPercent, double MemUsedMb, double DiskIops, double NetMbps) Sample();
}
