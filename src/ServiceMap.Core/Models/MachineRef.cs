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
}
