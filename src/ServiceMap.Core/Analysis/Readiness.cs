using ServiceMap.Core.Models;

namespace ServiceMap.Core.Analysis;

/// <summary>Inputs summarizing one machine's collected data.</summary>
public sealed class ReadinessInput
{
    /// <summary>Span between first and last observed flow, in days.</summary>
    public double WindowDays { get; set; }
    /// <summary>Total sampling sweeps recorded (meta sweep_count).</summary>
    public long SweepCount { get; set; }
    /// <summary>Fraction of distinct flows with process or service attribution (0-1).</summary>
    public double AttributionPct { get; set; }
    /// <summary>"local-collector", "remote-scan", or empty.</summary>
    public string CollectionSource { get; set; } = string.Empty;
    /// <summary>Distinct flows observed.</summary>
    public int FlowCount { get; set; }
}

/// <summary>Migration-readiness verdict for one machine.</summary>
public sealed class ReadinessResult
{
    public int Score { get; set; }                 // 0-100
    public string Rating { get; set; } = "";       // Ready / Partial / Insufficient
    public List<string> Notes { get; } = new();
    public override string ToString() => $"{Rating} ({Score})";
}

/// <summary>
/// Scores whether a machine has been observed long and well enough to plan its
/// migration from the data. Deliberately simple and explainable:
///   up to 45 pts for observation window length (full marks at 14 days),
///   up to 25 pts for sweep volume  (full marks at 500 sweeps),
///   up to 30 pts for process/service attribution coverage.
/// Ready >= 80, Partial >= 50, else Insufficient. Notes explain every deduction
/// so the score is never a black box.
/// </summary>
public static class Readiness
{
    public const double TargetWindowDays = 14;
    public const long TargetSweeps = 500;

    public static ReadinessResult Compute(ReadinessInput i)
    {
        var r = new ReadinessResult();
        if (i.FlowCount == 0)
        {
            r.Score = 0;
            r.Rating = "Insufficient";
            r.Notes.Add("No flows observed at all.");
            return r;
        }

        var windowPts = (int)Math.Round(Math.Clamp(i.WindowDays / TargetWindowDays, 0, 1) * 45);
        var sweepPts = (int)Math.Round(Math.Clamp((double)i.SweepCount / TargetSweeps, 0, 1) * 25);
        var attrPts = (int)Math.Round(Math.Clamp(i.AttributionPct, 0, 1) * 30);
        r.Score = windowPts + sweepPts + attrPts;

        if (i.WindowDays < TargetWindowDays)
            r.Notes.Add($"Observed {i.WindowDays:F1} days; {TargetWindowDays:F0}+ recommended to catch weekly/month-end patterns.");
        if (i.SweepCount < TargetSweeps)
            r.Notes.Add($"{i.SweepCount} sweeps recorded; sparse snapshots miss short-lived flows.");
        if (i.AttributionPct < 0.8)
            r.Notes.Add($"Only {i.AttributionPct:P0} of flows have process/service attribution (run the collector elevated).");
        if (i.CollectionSource == "remote-scan")
            r.Notes.Add("Data comes from agentless snapshots, not continuous collection.");

        r.Rating = r.Score >= 80 ? "Ready" : r.Score >= 50 ? "Partial" : "Insufficient";
        return r;
    }

    /// <summary>Build the input from a machine's flows and meta values.</summary>
    public static ReadinessInput FromFlows(IReadOnlyList<ConnectionAggregate> flows,
        long sweepCount, string collectionSource)
    {
        var input = new ReadinessInput
        {
            SweepCount = sweepCount,
            CollectionSource = collectionSource,
            FlowCount = flows.Count
        };
        if (flows.Count > 0)
        {
            input.WindowDays = (flows.Max(f => f.LastSeen) - flows.Min(f => f.FirstSeen)).TotalDays;
            input.AttributionPct = flows.Count(f => f.ServiceOrProcess.Length > 0) / (double)flows.Count;
        }
        return input;
    }
}
