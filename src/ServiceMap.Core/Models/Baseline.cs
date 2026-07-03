namespace ServiceMap.Core.Models;

/// <summary>A saved, labeled snapshot of the distinct flows at a point in time.</summary>
public sealed class Baseline
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public int FlowCount { get; set; }
}

/// <summary>Result of comparing current flows against a baseline.</summary>
public sealed class BaselineDiff
{
    public List<ConnectionAggregate> Added { get; } = new();
    public List<ConnectionAggregate> Removed { get; } = new();
    public int UnchangedCount { get; set; }
}
