namespace ServiceMap.Remote.Models;

/// <summary>Which remote-access path to use for a target.</summary>
public enum OsKind
{
    /// <summary>Try WinRM first, then SSH (best effort).</summary>
    Auto = 0,
    /// <summary>Windows host, collected over WinRM / PowerShell Remoting.</summary>
    Windows = 1,
    /// <summary>Linux host, collected over SSH.</summary>
    Linux = 2
}
