using ServiceMap.Firewall.Model;
using ServiceMap.Firewall.Parsing;

namespace ServiceMap.Firewall.Matching;

public enum FwCoverage { Covered, Gap, Denied, Unknown }

/// <summary>Result of reconciling one observed flow against policy.</summary>
public sealed class FwMatchResult
{
    public FwCoverage Coverage { get; set; }
    public string? RuleName { get; set; }
    public string? Policy { get; set; }
    public string? SourceZone { get; set; }
    public string? DestZone { get; set; }
}

/// <summary>One observed flow to reconcile. Protocol is "tcp" or "udp".</summary>
public sealed record FlowKey(string LocalIp, string RemoteIp, int LocalPort, int RemotePort,
                             bool Outbound, string Protocol = "tcp");

/// <summary>
/// A parsed, indexed firewall policy: address groups plus egress/ingress/on-prem
/// rules, with flow reconciliation and IP enrichment.
///
/// Coverage semantics (chosen deliberately): an allow in ANY loaded policy marks
/// a flow Covered (OR across policies), and unknown app-ids in
/// application-default rules match leniently. Both favor fewer false Gaps over
/// fewer false Covered.
/// </summary>
public sealed class FirewallPolicy
{
    public List<FwGroup> Groups { get; } = new();
    public List<FwRule> Egress { get; } = new();
    public List<FwRule> Ingress { get; } = new();
    public List<FwRule> OnPrem { get; } = new();     // Check Point
    public PolicyIndex Index { get; private set; } = new(Array.Empty<FwGroup>());

    /// <summary>
    /// Rule source/destination references that resolve to no network (FQDN
    /// objects, names without embedded IPs). Rules using only these cannot
    /// match any flow, so flows they cover surface as Gap — worth surfacing.
    /// </summary>
    public IReadOnlyList<string> UnresolvedReferences { get; private set; } = Array.Empty<string>();

    public int RuleCount => Egress.Count + Ingress.Count + OnPrem.Count;

    /// <summary>Build a policy from raw CSV texts (any may be null/empty).</summary>
    public static FirewallPolicy Load(string? egressCsv, string? ingressCsv, string? checkpointCsv, string? groupsCsv) =>
        Load(TextList(egressCsv), TextList(ingressCsv), TextList(checkpointCsv), TextList(groupsCsv));

    private static IReadOnlyList<string> TextList(string? s) =>
        string.IsNullOrEmpty(s) ? Array.Empty<string>() : new[] { s };

    /// <summary>
    /// Build a policy from several CSV exports per category (multi-region
    /// Panorama exports, several Check Point packages). Each file is parsed
    /// independently so headers never bleed into rule rows.
    /// </summary>
    public static FirewallPolicy Load(
        IReadOnlyList<string> egressCsvs, IReadOnlyList<string> ingressCsvs,
        IReadOnlyList<string> checkpointCsvs, IReadOnlyList<string> groupsCsvs)
    {
        var p = new FirewallPolicy();
        foreach (var t in groupsCsvs) p.Groups.AddRange(AddressGroupParser.Parse(t));
        foreach (var t in egressCsvs) p.Egress.AddRange(PaloAltoRuleParser.Parse(t, "CWAN Egress"));
        foreach (var t in ingressCsvs) p.Ingress.AddRange(PaloAltoRuleParser.Parse(t, "CWAN Ingress"));
        foreach (var t in checkpointCsvs) p.OnPrem.AddRange(CheckpointRuleParser.Parse(t));
        p.Index = new PolicyIndex(p.Groups, p.Egress.Concat(p.Ingress).Concat(p.OnPrem));

        p.UnresolvedReferences = p.Egress.Concat(p.Ingress).Concat(p.OnPrem)
            .SelectMany(r => r.Sources.Concat(r.Destinations))
            .Where(x => !x.Equals("any", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => p.Index.Resolve(x).Count == 0)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return p;
    }

    public EnrichResult Enrich(string ip) =>
        IpUtil.TryParse(ip, out var v) ? Index.Enrich(v) : new EnrichResult();

    /// <summary>Reconcile a flow against the relevant policies; Denied &gt; Covered &gt; Gap.</summary>
    public FwMatchResult MatchFlow(FlowKey f)
    {
        if (!IpUtil.TryParse(f.LocalIp, out var local) || !IpUtil.TryParse(f.RemoteIp, out var remote))
            return new FwMatchResult { Coverage = FwCoverage.Unknown };

        uint src, dst; int port;
        List<FwRule> primary;
        if (f.Outbound) { src = local; dst = remote; port = f.RemotePort; primary = Egress; }
        else            { src = remote; dst = local; port = f.LocalPort; primary = Ingress; }

        var proto = f.Protocol.ToLowerInvariant();
        var matches = new[] { Evaluate(primary, src, dst, port, proto), Evaluate(OnPrem, src, dst, port, proto) }
            .Where(m => m is not null).Select(m => m!).ToList();

        // A specific allow anywhere means the flow is covered (OR semantics).
        var allow = matches.FirstOrDefault(m => m.IsAllow);
        if (allow is not null)
            return new FwMatchResult
            {
                Coverage = FwCoverage.Covered, RuleName = allow.Name, Policy = allow.Policy,
                SourceZone = allow.SourceZone, DestZone = allow.DestZone
            };

        // A specific (non catch-all) deny is a real block worth flagging.
        var deny = matches.FirstOrDefault(m => !m.IsAllow && !IsCatchAll(m));
        if (deny is not null)
            return new FwMatchResult
            {
                Coverage = FwCoverage.Denied, RuleName = deny.Name, Policy = deny.Policy,
                SourceZone = deny.SourceZone, DestZone = deny.DestZone
            };

        // Otherwise only a catch-all cleanup (or nothing) matched: no specific rule exists.
        return new FwMatchResult { Coverage = FwCoverage.Gap };
    }

    /// <summary>
    /// Allow rules whose source or destination covers the machine's IP —
    /// candidates for reverse reconciliation ("which of my allows were never
    /// exercised during the observation window?").
    /// </summary>
    public IReadOnlyList<FwRule> AllowRulesCovering(string machineIp)
    {
        if (!IpUtil.TryParse(machineIp, out var ip)) return Array.Empty<FwRule>();
        return Egress.Concat(Ingress).Concat(OnPrem)
            .Where(r => r.IsAllow && !IsCatchAll(r) &&
                        (EndpointMatches(r.Sources, ip) || EndpointMatches(r.Destinations, ip)))
            .ToList();
    }

    /// <summary>First rule (in order) whose source, destination and service all match.</summary>
    private FwRule? Evaluate(List<FwRule> rules, uint src, uint dst, int port, string proto)
    {
        foreach (var rule in rules)
        {
            if (rule.Action == FwAction.Other) continue;
            if (!EndpointMatches(rule.Sources, src)) continue;
            if (!EndpointMatches(rule.Destinations, dst)) continue;
            if (!ServiceMatches(rule, port, proto)) continue;
            return rule;
        }
        return null;
    }

    private static bool IsCatchAll(FwRule r) => AllAny(r.Sources) && AllAny(r.Destinations);
    private static bool AllAny(List<string> refs) =>
        refs.Count == 0 || refs.All(x => x.Equals("any", StringComparison.OrdinalIgnoreCase));

    private bool EndpointMatches(List<string> refs, uint ip)
    {
        if (refs.Count == 0) return true;
        foreach (var r in refs)
        {
            if (r.Equals("any", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var c in Index.Resolve(r)) if (c.Contains(ip)) return true;
        }
        return false;
    }

    private static bool ServiceMatches(FwRule rule, int port, string proto)
    {
        if (rule.Services.Any(s => s.Equals("any", StringComparison.OrdinalIgnoreCase))) return true;
        foreach (var s in rule.Services)
            if (ServicePorts.Matches(s, port, proto)) return true;

        var appDefault = rule.Services.Count == 0 ||
                         rule.Services.Any(s => s.Equals("application-default", StringComparison.OrdinalIgnoreCase));
        if (appDefault)
        {
            if (rule.Applications.Count == 0 ||
                rule.Applications.Any(a => a.Equals("any", StringComparison.OrdinalIgnoreCase))) return true;
            foreach (var a in rule.Applications)
            {
                var ports = ServicePorts.PortsFor(a);
                if (ports.Length == 0) return true;         // unknown app-id: lenient by design
                if (ports.Contains(port)) return true;
            }
        }
        return false;
    }
}
