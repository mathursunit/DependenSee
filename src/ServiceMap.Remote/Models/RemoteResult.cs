using ServiceMap.Core.Models;

namespace ServiceMap.Remote.Models;

/// <summary>Outcome of collecting from one remote host.</summary>
public sealed class RemoteResult
{
    /// <summary>How many snapshots this session took (burst sweeps).</summary>
    public int SweepCount { get; set; } = 1;

    public string Host { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public OsKind Os { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long DurationMs { get; set; }
    public string? StoredPath { get; set; }

    public List<ServiceRecord> Services { get; } = new();
    public List<ConnectionSample> Connections { get; } = new();

    public static RemoteResult Fail(string host, string error) =>
        new() { Host = host, Success = false, Error = error };
}
