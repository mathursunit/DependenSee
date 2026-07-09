using System.Text.RegularExpressions;

namespace ServiceMap.Firewall.Matching;

/// <summary>IPv4 helpers over uint.</summary>
public static class IpUtil
{
    public static bool TryParse(string s, out uint ip)
    {
        ip = 0;
        var parts = s.Split('.');
        if (parts.Length != 4) return false;
        uint v = 0;
        foreach (var p in parts)
        {
            if (!byte.TryParse(p, out var b)) return false;
            v = (v << 8) | b;
        }
        ip = v;
        return true;
    }

    public static string ToStr(uint ip) => $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
}

/// <summary>An IPv4 network as base + prefix length.</summary>
public readonly struct Cidr
{
    public readonly uint Network;
    public readonly int Prefix;

    public Cidr(uint ip, int prefix)
    {
        Prefix = Math.Clamp(prefix, 0, 32);
        var mask = Prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - Prefix);
        Network = ip & mask;
    }

    public bool Contains(uint ip)
    {
        var mask = Prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - Prefix);
        return (ip & mask) == Network;
    }

    public override string ToString() => $"{IpUtil.ToStr(Network)}/{Prefix}";
}

/// <summary>
/// Extracts an IPv4 network from a firewall reference. Handles bare CIDRs
/// (10.0.0.0/24), object names that embed an IP (cmusswhadpdc01-10.94.9.167 =&gt; /32),
/// and names that embed a masked network (Name-10.181.44.0_22 =&gt; /22).
/// </summary>
public static class AddressExtractor
{
    private static readonly Regex Rx = new(
        @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})(?:[_/](\d{1,2}))?",
        RegexOptions.Compiled);

    public static bool TryExtract(string reference, out Cidr cidr)
    {
        cidr = default;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        var m = Rx.Match(reference);
        if (!m.Success) return false;
        if (!IpUtil.TryParse(m.Groups[1].Value, out var ip)) return false;
        var prefix = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var p) ? p : 32;
        cidr = new Cidr(ip, prefix);
        return true;
    }
}
