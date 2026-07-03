namespace ServiceMap.Core.Models;

/// <summary>What an annotation is attached to.</summary>
public enum AnnotationKind
{
    Process = 0,
    Port = 1,
    Host = 2
}

/// <summary>Business criticality for migration planning.</summary>
public enum Criticality
{
    Unset = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// A user-authored label attached to a process name, a port, or a remote host —
/// e.g. "SQL Server (Finance DB)", owner "J. Lee", criticality High. Stored in the
/// GUI workspace, not the collector database.
/// </summary>
public sealed class Annotation
{
    public AnnotationKind Kind { get; set; }
    /// <summary>The process name, port number (as text), or host address.</summary>
    public string Key { get; set; } = string.Empty;
    public string? FriendlyName { get; set; }
    public string? Owner { get; set; }
    public Criticality Criticality { get; set; }
    public string? Notes { get; set; }
    public DateTime Updated { get; set; }
}
