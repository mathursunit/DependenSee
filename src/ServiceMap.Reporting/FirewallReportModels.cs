using ServiceMap.Core.Models;

namespace ServiceMap.Reporting;

/// <summary>Options controlling how the firewall report is built.</summary>
public sealed class FirewallReportOptions
{
    /// <summary>Best-effort reverse-DNS on peer addresses (with a short timeout).</summary>
    public bool ResolveHostnames { get; set; } = true;

    /// <summary>Collapse many inbound sources in the same /24 into one CIDR entry.</summary>
    public bool SummarizeSourcesToCidr { get; set; } = false;

    /// <summary>Absolute path to a logo image to place on the cover, if present.</summary>
    public string? LogoPath { get; set; }

    /// <summary>Short description of the filters used, shown in the header.</summary>
    public string FilterSummary { get; set; } = "All observed traffic";

    /// <summary>Optional annotation lookup ("kind:key" -> Annotation) to enrich rules.</summary>
    public IReadOnlyDictionary<string, Annotation>? Annotations { get; set; }
}

/// <summary>A remote peer with its scope and optional resolved hostname.</summary>
public sealed class PeerRef
{
    public string Address { get; set; } = string.Empty;
    public IpScope Scope { get; set; }
    public string? Host { get; set; }
}

/// <summary>An inbound allow-rule: who may reach a local service port.</summary>
public sealed class InboundRule
{
    public Protocol Protocol { get; set; }
    public int LocalPort { get; set; }
    public string Process { get; set; } = string.Empty;
    public List<PeerRef> Sources { get; } = new();
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public long Occurrences { get; set; }
    /// <summary>False when the port is listening but no inbound traffic was seen.</summary>
    public bool ObservedTraffic { get; set; }
    /// <summary>Annotation enrichment (friendly name / owner / criticality), if any.</summary>
    public string? Note { get; set; }
}

/// <summary>An outbound allow-rule: a destination this machine reaches.</summary>
public sealed class OutboundRule
{
    public Protocol Protocol { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string Process { get; set; } = string.Empty;
    public IpScope Scope { get; set; }
    public string? RemoteHost { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public long Occurrences { get; set; }
    public string? Note { get; set; }
}

/// <summary>The complete report model handed to the PDF renderer.</summary>
public sealed class FirewallReport
{
    public string MachineName { get; set; } = string.Empty;
    public List<string> MachineAddresses { get; } = new();
    public DateTime? WindowStart { get; set; }
    public DateTime? WindowEnd { get; set; }
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string FilterSummary { get; set; } = string.Empty;
    public string? LogoPath { get; set; }

    public List<InboundRule> Inbound { get; } = new();
    public List<OutboundRule> Outbound { get; } = new();

    public int InternetOutboundCount =>
        Outbound.Count(o => o.Scope == IpScope.Public);
    public int InternetInboundCount =>
        Inbound.Count(i => i.Sources.Any(s => s.Scope == IpScope.Public));
}
