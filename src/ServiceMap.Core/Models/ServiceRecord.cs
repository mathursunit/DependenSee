namespace ServiceMap.Core.Models;

/// <summary>
/// A registered OS service (Windows Service Control Manager entry, or a Linux
/// systemd unit once the Linux platform is implemented).
/// </summary>
public sealed class ServiceRecord
{
    /// <summary>Short service name (e.g. "Dnscache").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Friendly display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Current run state (Running, Stopped, ...).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Start mode (Auto, Manual, Disabled, ...).</summary>
    public string StartMode { get; set; } = string.Empty;

    /// <summary>Owning process id, or 0 when the service is not running.</summary>
    public int ProcessId { get; set; }

    /// <summary>Executable/command backing the service, if known.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Account the service runs under, if known.</summary>
    public string? Account { get; set; }

    /// <summary>UTC time this snapshot was taken.</summary>
    public DateTime ScanTimestamp { get; set; }
}
