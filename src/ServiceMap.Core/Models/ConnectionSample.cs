using System.Net;

namespace ServiceMap.Core.Models;

/// <summary>
/// A single observation of a socket at a point in time: either a listening
/// endpoint or an active connection, attributed to the owning process.
/// </summary>
public sealed class ConnectionSample
{
    public long Id { get; set; }

    public Protocol Protocol { get; set; }

    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }

    /// <summary>Remote address; empty for listeners and UDP endpoints.</summary>
    public string RemoteAddress { get; set; } = string.Empty;
    /// <summary>Remote port; 0 for listeners and UDP endpoints.</summary>
    public int RemotePort { get; set; }

    /// <summary>TCP state; <see cref="TcpState.Unknown"/> for UDP.</summary>
    public TcpState State { get; set; }

    public ConnectionDirection Direction { get; set; }

    /// <summary>Scope of the remote address (private, internet, loopback, ...).</summary>
    public IpScope RemoteScope { get; set; }

    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? ProcessPath { get; set; }

    /// <summary>Owning machine (set when unioning multiple databases in the fleet view).</summary>
    public string Machine { get; set; } = string.Empty;

    /// <summary>Owning Windows service (resolved from PID at collection time); empty for non-service processes.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Service name if known, otherwise the process name.</summary>
    public string ServiceOrProcess => string.IsNullOrEmpty(ServiceName) ? ProcessName : ServiceName;

    /// <summary>UTC time this observation was taken.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Local-time display form of <see cref="Timestamp"/>. Grids bind this
    /// pre-formatted string directly: binding DateTime cells through an
    /// IValueConverter proved unreliable in the DataGrid, rendering default
    /// dates (0001-01-01) despite correct data.
    /// </summary>
    public string TimestampLocal => Timestamp == default
        ? string.Empty
        : Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Convenience "addr:port" rendering of the local endpoint.</summary>
    public string LocalEndpoint => FormatEndpoint(LocalAddress, LocalPort);

    /// <summary>Convenience "addr:port" rendering of the remote endpoint.</summary>
    public string RemoteEndpoint =>
        string.IsNullOrEmpty(RemoteAddress) ? string.Empty : FormatEndpoint(RemoteAddress, RemotePort);

    private static string FormatEndpoint(string addr, int port)
    {
        // Bracket IPv6 literals for readability.
        if (IPAddress.TryParse(addr, out var ip) &&
            ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return $"[{addr}]:{port}";
        }
        return $"{addr}:{port}";
    }
}
