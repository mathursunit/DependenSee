namespace ServiceMap.Core.Models;

/// <summary>An imported machine in the fleet workspace (a collector database).</summary>
public sealed class MachineRef
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DatabasePath { get; set; } = string.Empty;
    /// <summary>Migration wave label, or empty if unassigned.</summary>
    public string Wave { get; set; } = string.Empty;
    public DateTime Added { get; set; }

    // ---- Computed for display when the fleet list loads (not persisted) ----

    /// <summary>"Ready (86)" / "Partial (62)" / "Insufficient (18)".</summary>
    public string ReadinessText { get; set; } = string.Empty;
    public int ReadinessScore { get; set; }

    /// <summary>Dependencies first observed in the last 7 days.</summary>
    public int NewDeps7d { get; set; }

    /// <summary>"local-collector" / "remote-scan" / "".</summary>
    public string Source { get; set; } = string.Empty;
}
