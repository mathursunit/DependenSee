namespace ServiceMap.Core.Models;

/// <summary>
/// A distinct connection "flow" collapsed from many samples — one logical
/// dependency between this machine and a peer, with first/last seen and how
/// many times it was observed. Produced by the unique-connections query and
/// consumed by the History view and the firewall report.
/// </summary>
public sealed class ConnectionAggregate
{
    public Protocol Protocol { get; set; }
    public ConnectionDirection Direction { get; set; }
    public IpScope RemoteScope { get; set; }

    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }

    public string ProcessName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;

    public string ServiceOrProcess => string.IsNullOrEmpty(ServiceName) ? ProcessName : ServiceName;

    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public long SampleCount { get; set; }

    public string LocalEndpoint =>
        LocalPort == 0 ? LocalAddress : $"{LocalAddress}:{LocalPort}";
    public string RemoteEndpoint =>
        string.IsNullOrEmpty(RemoteAddress) ? string.Empty :
        (RemotePort == 0 ? RemoteAddress : $"{RemoteAddress}:{RemotePort}");
}
