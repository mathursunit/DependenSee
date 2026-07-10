using ServiceMap.Core.Models;

namespace ServiceMap.Core.Analysis;

/// <summary>A modernization/security finding derived from observed traffic.</summary>
public sealed class RiskFinding
{
    public string Severity { get; set; } = "Medium";   // High / Medium / Low
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// Cheap, explainable heuristics over data already collected: deprecated or
/// risky protocols and internet exposure that cloud security review will flag
/// anyway — better to surface them during migration planning.
/// </summary>
public static class RiskFlags
{
    private sealed record PortRule(int Port, string Proto, string Severity, string Name, string Advice);

    private static readonly PortRule[] ServerPorts =
    {
        new(23,  "tcp", "High",   "Telnet",            "cleartext administration; replace with SSH"),
        new(21,  "tcp", "High",   "FTP",               "cleartext file transfer; replace with SFTP/FTPS"),
        new(512, "tcp", "High",   "rexec",             "legacy r-service; remove before migration"),
        new(513, "tcp", "High",   "rlogin",            "legacy r-service; remove before migration"),
        new(514, "tcp", "High",   "rsh",               "legacy r-service; remove before migration"),
        new(389, "tcp", "Medium", "LDAP (cleartext)",  "prefer LDAPS 636 or signed LDAP"),
        new(80,  "tcp", "Low",    "HTTP (cleartext)",  "prefer HTTPS for anything crossing networks"),
        new(137, "udp", "Medium", "NetBIOS name svc",  "legacy name resolution; unnecessary in cloud"),
        new(138, "udp", "Medium", "NetBIOS datagram",  "legacy; unnecessary in cloud"),
        new(139, "tcp", "Medium", "NetBIOS session",   "legacy SMB transport; use 445 or retire"),
        new(1433,"tcp", "Low",    "SQL Server",        "ensure TLS is forced before exposing across networks"),
    };

    public static List<RiskFinding> Analyze(IReadOnlyList<ConnectionAggregate> flows)
    {
        var findings = new List<RiskFinding>();

        foreach (var rule in ServerPorts)
        {
            // Serving the risky protocol: inbound traffic to (or a listener on) the port.
            var inbound = flows.Where(f =>
                f.Protocol.ToString().Equals(rule.Proto, StringComparison.OrdinalIgnoreCase) &&
                f.LocalPort == rule.Port &&
                f.Direction is ConnectionDirection.Inbound or ConnectionDirection.Listen).ToList();
            var served = inbound.Any(f => f.Direction == ConnectionDirection.Inbound);
            var listening = inbound.Any(f => f.Direction == ConnectionDirection.Listen);
            if (served || listening)
            {
                findings.Add(new RiskFinding
                {
                    Severity = rule.Severity,
                    Title = $"{rule.Name} served on port {rule.Port}",
                    Detail = (served
                        ? $"Inbound {rule.Name} traffic observed ({inbound.Where(f => f.Direction == ConnectionDirection.Inbound).Sum(f => f.SampleCount)} samples)"
                        : "Listening, no inbound traffic observed") + " - " + rule.Advice + "."
                });
            }

            // Consuming it from somewhere else.
            var outbound = flows.Where(f =>
                f.Protocol.ToString().Equals(rule.Proto, StringComparison.OrdinalIgnoreCase) &&
                f.RemotePort == rule.Port &&
                f.Direction == ConnectionDirection.Outbound).ToList();
            if (outbound.Count > 0 && rule.Severity != "Low")
            {
                var targets = string.Join(", ", outbound.Select(f => f.RemoteAddress).Distinct().Take(5));
                findings.Add(new RiskFinding
                {
                    Severity = rule.Severity,
                    Title = $"Outbound {rule.Name} to {outbound.Select(f => f.RemoteAddress).Distinct().Count()} host(s)",
                    Detail = $"Targets: {targets} - {rule.Advice}."
                });
            }
        }

        // Internet exposure: inbound traffic from public addresses.
        var internetInbound = flows
            .Where(f => f.Direction == ConnectionDirection.Inbound && f.RemoteScope == IpScope.Public)
            .GroupBy(f => f.LocalPort)
            .ToList();
        foreach (var g in internetInbound)
        {
            findings.Add(new RiskFinding
            {
                Severity = "High",
                Title = $"Internet-facing service on port {g.Key}",
                Detail = $"Inbound traffic from {g.Select(f => f.RemoteAddress).Distinct().Count()} public address(es); " +
                         "confirm this exposure is intended before replicating it in the target network."
            });
        }

        return findings
            .OrderBy(f => f.Severity switch { "High" => 0, "Medium" => 1, _ => 2 })
            .ThenBy(f => f.Title)
            .ToList();
    }

    /// <summary>Dependencies whose first observation is within the last <paramref name="days"/> days — drift during a change freeze.</summary>
    public static List<ConnectionAggregate> RecentlyAppeared(IReadOnlyList<ConnectionAggregate> flows, int days = 7)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        return flows
            .Where(f => f.Direction is ConnectionDirection.Inbound or ConnectionDirection.Outbound)
            .Where(f => f.FirstSeen >= cutoff)
            .OrderByDescending(f => f.FirstSeen)
            .ToList();
    }
}
