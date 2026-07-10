namespace ServiceMap.Core.Models;

/// <summary>
/// A distinct (process, queried DNS name, resolved IP) observed via the DNS
/// client, folded like connection_flows. IPs change in a migration but names
/// endure — this is the raw material for FQDN-based egress rules.
/// </summary>
public sealed class DnsResolution
{
    public string ProcessName { get; set; } = string.Empty;
    public string QueryName { get; set; } = string.Empty;
    public string ResolvedAddress { get; set; } = string.Empty;
    public long Count { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}
