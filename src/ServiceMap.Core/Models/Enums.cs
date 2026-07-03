namespace ServiceMap.Core.Models;

/// <summary>Transport protocol for a sampled socket.</summary>
public enum Protocol
{
    Tcp = 0,
    Udp = 1
}

/// <summary>
/// Direction of a connection relative to this host. Inferred from whether the
/// local port is one this host is listening on.
/// </summary>
public enum ConnectionDirection
{
    /// <summary>A listening socket (server endpoint), not an active connection.</summary>
    Listen = 0,
    /// <summary>Remote peer connected in to a local listening port.</summary>
    Inbound = 1,
    /// <summary>This host initiated the connection to a remote endpoint.</summary>
    Outbound = 2,
    /// <summary>Direction could not be determined.</summary>
    Unknown = 3
}

/// <summary>
/// Classification of a remote address relative to the machine, used for the
/// private-vs-internet filter and firewall reporting.
/// </summary>
public enum IpScope
{
    /// <summary>No remote address (a listener) or address could not be parsed.</summary>
    None = 0,
    /// <summary>Loopback (127.0.0.0/8, ::1).</summary>
    Loopback = 1,
    /// <summary>Link-local (169.254.0.0/16, fe80::/10).</summary>
    LinkLocal = 2,
    /// <summary>RFC1918 / unique-local / CGNAT — internal network.</summary>
    Private = 3,
    /// <summary>Routable public address — the internet.</summary>
    Public = 4
}

/// <summary>Address-family choice for the viewer's IPv4/IPv6 filter.</summary>
public enum AddressFamilyOption
{
    Any = 0,
    IPv4 = 1,
    IPv6 = 2
}

/// <summary>TCP connection state (subset of the Win32 MIB_TCP_STATE values).</summary>
public enum TcpState
{
    Unknown = 0,
    Closed = 1,
    Listen = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAck = 10,
    TimeWait = 11,
    DeleteTcb = 12
}
