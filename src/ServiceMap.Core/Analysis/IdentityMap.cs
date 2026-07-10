using ServiceMap.Core.Models;

namespace ServiceMap.Core.Analysis;

/// <summary>An identity/authentication dependency inferred from observed traffic.</summary>
public sealed class IdentityDependency
{
    public string Kind { get; set; } = string.Empty;     // Kerberos / LDAP / LDAPS / NTLM-ish / DNS / Global Catalog
    public string RemoteAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public long SampleCount { get; set; }
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// Extracts identity/authentication dependencies — the surprise that delays
/// cutover weekends. Machines leaving the on-prem network need line-of-sight to
/// a domain controller (or a re-plumbed identity plane). Classifies observed
/// outbound flows to the well-known AD/identity ports, plus non-LocalSystem
/// service accounts that will not exist in the target.
/// </summary>
public static class IdentityMap
{
    private sealed record IdPort(int Port, string Proto, string Kind, string Note);

    private static readonly IdPort[] Ports =
    {
        new(88,   "tcp", "Kerberos",       "domain authentication (KDC) — needs a reachable DC in target"),
        new(88,   "udp", "Kerberos",       "domain authentication (KDC)"),
        new(389,  "tcp", "LDAP",           "directory lookups — needs a reachable DC"),
        new(389,  "udp", "LDAP (CLDAP)",   "DC locator pings"),
        new(636,  "tcp", "LDAPS",          "secure directory lookups"),
        new(3268, "tcp", "Global Catalog", "forest-wide directory — GC must be reachable"),
        new(3269, "tcp", "Global Catalog (SSL)", "secure GC"),
        new(464,  "tcp", "Kerberos passwd","password change (kpasswd)"),
        new(445,  "tcp", "SMB/Netlogon",   "SYSVOL / group policy / secure channel to DC"),
        new(135,  "tcp", "RPC endpoint mapper", "DCOM/RPC to domain services"),
    };

    /// <summary>Identity dependencies from a machine's outbound flows.</summary>
    public static List<IdentityDependency> FromFlows(IReadOnlyList<ConnectionAggregate> flows)
    {
        var deps = new List<IdentityDependency>();
        foreach (var f in flows.Where(f => f.Direction == ConnectionDirection.Outbound))
        {
            var proto = f.Protocol.ToString().ToLowerInvariant();
            var match = Ports.FirstOrDefault(p => p.Port == f.RemotePort && p.Proto == proto);
            if (match is null) continue;
            deps.Add(new IdentityDependency
            {
                Kind = match.Kind,
                RemoteAddress = f.RemoteAddress,
                Port = f.RemotePort,
                Protocol = f.Protocol.ToString(),
                SampleCount = f.SampleCount,
                Note = match.Note
            });
        }
        return deps
            .GroupBy(d => (d.Kind, d.RemoteAddress, d.Port))
            .Select(g => { var d = g.First(); d.SampleCount = g.Sum(x => x.SampleCount); return d; })
            .OrderBy(d => d.RemoteAddress).ThenBy(d => d.Port)
            .ToList();
    }

    /// <summary>
    /// Service accounts that are not built-in and therefore won't exist in the
    /// target as-is (domain or custom accounts needing gMSA/managed identity).
    /// </summary>
    public static List<ServiceRecord> NonBuiltinServiceAccounts(IReadOnlyList<ServiceRecord> services)
    {
        return services
            .Where(s => !string.IsNullOrWhiteSpace(s.Account) && !IsBuiltin(s.Account!))
            .OrderBy(s => s.Account).ThenBy(s => s.Name)
            .ToList();
    }

    private static bool IsBuiltin(string account)
    {
        var a = account.Trim();
        return a.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase)
            || a.Equals("NT AUTHORITY\\LocalService", StringComparison.OrdinalIgnoreCase)
            || a.Equals("NT AUTHORITY\\NetworkService", StringComparison.OrdinalIgnoreCase)
            || a.Equals("NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase)
            || a.Equals("LocalService", StringComparison.OrdinalIgnoreCase)
            || a.Equals("NetworkService", StringComparison.OrdinalIgnoreCase)
            || a.StartsWith("NT SERVICE\\", StringComparison.OrdinalIgnoreCase);
    }
}
