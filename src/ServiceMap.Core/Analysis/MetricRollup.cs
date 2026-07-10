namespace ServiceMap.Core.Analysis;

/// <summary>One resource-utilization sample (a point in time).</summary>
public sealed record MetricSample(DateTime TimestampUtc, double CpuPercent, double MemUsedMb,
    double DiskIops, double NetMbps);

/// <summary>Percentile/peak rollup of a metric series, for target right-sizing.</summary>
public sealed class MetricSummary
{
    public int SampleCount { get; set; }
    public double CpuAvg { get; set; }
    public double CpuP95 { get; set; }
    public double CpuPeak { get; set; }
    public double MemAvgMb { get; set; }
    public double MemP95Mb { get; set; }
    public double MemPeakMb { get; set; }
    public double DiskP95Iops { get; set; }
    public double NetP95Mbps { get; set; }
}

/// <summary>
/// Summarizes resource-utilization samples into the peak/95th-percentile figures
/// an instance-sizing decision needs (spikes matter more than averages for
/// choosing a target instance type).
/// </summary>
public static class MetricRollup
{
    public static MetricSummary Summarize(IReadOnlyList<MetricSample> samples)
    {
        var s = new MetricSummary { SampleCount = samples.Count };
        if (samples.Count == 0) return s;

        s.CpuAvg = samples.Average(x => x.CpuPercent);
        s.CpuP95 = Percentile(samples.Select(x => x.CpuPercent), 0.95);
        s.CpuPeak = samples.Max(x => x.CpuPercent);
        s.MemAvgMb = samples.Average(x => x.MemUsedMb);
        s.MemP95Mb = Percentile(samples.Select(x => x.MemUsedMb), 0.95);
        s.MemPeakMb = samples.Max(x => x.MemUsedMb);
        s.DiskP95Iops = Percentile(samples.Select(x => x.DiskIops), 0.95);
        s.NetP95Mbps = Percentile(samples.Select(x => x.NetMbps), 0.95);
        return s;
    }

    /// <summary>Linear-interpolated percentile (0-1).</summary>
    public static double Percentile(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];
        var rank = p * (sorted.Length - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (rank - lo) * (sorted[hi] - sorted[lo]);
    }
}
