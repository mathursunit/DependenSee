namespace ServiceMap.Remote.Models;

/// <summary>
/// A single host (or expanded member of a range) to collect from remotely,
/// together with the credentials needed. Passwords are held in memory only for
/// the duration of a scan; persisted targets store them DPAPI-encrypted.
/// </summary>
public sealed class RemoteTarget
{
    public string Host { get; set; } = string.Empty;
    public OsKind Os { get; set; } = OsKind.Auto;

    /// <summary>Port override; 0 uses the protocol default (22 SSH, 5985 WinRM).</summary>
    public int Port { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>Plaintext password, in memory only. Null when using a key file.</summary>
    public string? Password { get; set; }

    /// <summary>Path to an SSH private key (Linux), used instead of a password.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Snapshots taken per session. A single snapshot misses anything that
    /// opens and closes between scans; several sweeps a few seconds apart in
    /// the same logon multiply capture density at negligible cost. Clamped 1-10.
    /// </summary>
    public int SweepCount { get; set; } = 3;

    /// <summary>Seconds between sweeps within one session. Clamped 1-60.</summary>
    public int SweepDelaySeconds { get; set; } = 10;

    public int ResolvedPort(bool ssh) => Port > 0 ? Port : (ssh ? 22 : 5985);

    public RemoteTarget Clone() => (RemoteTarget)MemberwiseClone();
}
