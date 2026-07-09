using ServiceMap.Firewall.Model;

namespace ServiceMap.Firewall.Matching;

/// <summary>Resolved enrichment for an IP: the most specific named object and all groups it belongs to.</summary>
public sealed class EnrichResult
{
    public string? ObjectName { get; set; }
    public string? Cidr { get; set; }
    public List<string> Groups { get; } = new();
    public bool Found => ObjectName is not null || Groups.Count > 0;
}

/// <summary>
/// Flattens address groups and indexes every named reference that embeds an IP,
/// so an observed IP can be resolved to firewall object/group names and any rule
/// reference (object, nested group, or literal CIDR) can be resolved to networks.
/// </summary>
public sealed class PolicyIndex
{
    private readonly Dictionary<string, FwGroup> _groups;
    private readonly List<(Cidr Cidr, string Name, bool IsGroup)> _entries = new();
    private readonly Dictionary<string, List<Cidr>> _resolveCache = new(StringComparer.OrdinalIgnoreCase);

    public PolicyIndex(IEnumerable<FwGroup> groups)
        : this(groups, Array.Empty<Model.FwRule>()) { }

    public PolicyIndex(IEnumerable<FwGroup> groups, IEnumerable<Model.FwRule> rules)
    {
        _groups = new Dictionary<string, FwGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            if (_groups.TryGetValue(g.Name, out var existing)) existing.Members.AddRange(g.Members);
            else _groups[g.Name] = g;
        }

        // Index every group and its members that embed an IP.
        foreach (var g in _groups.Values)
        {
            foreach (var member in g.Members)
            {
                if (AddressExtractor.TryExtract(member, out var c))
                {
                    _entries.Add((c, StripIp(member), false));   // the object itself
                    _entries.Add((c, g.Name, true));              // its group
                }
            }
        }
        // Also index object names that appear directly in rule sources/destinations
        // (many hosts are named in rules, not groups) so their IPs resolve.
        foreach (var r in rules)
            foreach (var reference in r.Sources.Concat(r.Destinations))
                if (AddressExtractor.TryExtract(reference, out var rc))
                    _entries.Add((rc, StripIp(reference), false));

        // Most specific first for enrichment.
        _entries.Sort((a, b) => b.Cidr.Prefix.CompareTo(a.Cidr.Prefix));
    }

    /// <summary>Resolve a rule reference (object/group/CIDR) to the networks it covers.</summary>
    public List<Cidr> Resolve(string reference)
    {
        if (_resolveCache.TryGetValue(reference, out var cached)) return cached;
        var result = new List<Cidr>();
        ResolveInto(reference, result, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        _resolveCache[reference] = result;
        return result;
    }

    private void ResolveInto(string reference, List<Cidr> into, HashSet<string> seen)
    {
        if (!seen.Add(reference)) return;                 // cycle guard
        if (AddressExtractor.TryExtract(reference, out var c)) { into.Add(c); return; }
        if (_groups.TryGetValue(reference, out var g))
            foreach (var m in g.Members) ResolveInto(m, into, seen);
        // else: unresolved object/FQDN — contributes nothing.
    }

    /// <summary>Resolve an IP to its most specific object and containing groups.</summary>
    public EnrichResult Enrich(uint ip)
    {
        var res = new EnrichResult();
        foreach (var (cidr, name, isGroup) in _entries)
        {
            if (!cidr.Contains(ip)) continue;
            if (isGroup)
            {
                if (!res.Groups.Contains(name)) res.Groups.Add(name);
            }
            else if (res.ObjectName is null)
            {
                res.ObjectName = name;
                res.Cidr = cidr.ToString();
            }
        }
        return res;
    }

    private static string StripIp(string member)
    {
        // "cmusswhadpdc01-10.94.9.167" -> "cmusswhadpdc01"; keep whole if no dash-IP.
        var idx = member.LastIndexOf('-');
        if (idx > 0 && AddressExtractor.TryExtract(member[(idx + 1)..], out _)) return member[..idx];
        return member;
    }
}
