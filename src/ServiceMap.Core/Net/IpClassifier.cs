using System.Net;
using System.Net.Sockets;
using ServiceMap.Core.Models;

namespace ServiceMap.Core.Net;

/// <summary>
/// Classifies IP addresses into a <see cref="IpScope"/> (private vs. internet)
/// and reports address family. Pure, dependency-free, and reused by both the
/// storage layer (to stamp each sample) and the firewall report.
/// </summary>
public static class IpClassifier
{
    /// <summary>
    /// Classify a remote address string. Empty/unparseable input, and any
    /// wildcard/"no peer" value, returns <see cref="IpScope.None"/>.
    /// </summary>
    public static IpScope Classify(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return IpScope.None;
        if (!IPAddress.TryParse(address, out var ip)) return IpScope.None;
        return Classify(ip);
    }

    public static IpScope Classify(IPAddress ip)
    {
        // Normalize v4-mapped IPv6 (::ffff:a.b.c.d) down to the IPv4 view.
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip)) return IpScope.Loopback;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return ClassifyV4(ip.GetAddressBytes());

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ClassifyV6(ip);

        return IpScope.None;
    }

    private static IpScope ClassifyV4(byte[] b)
    {
        int a = b[0], c = b[1];

        // Unspecified / current network.
        if (a == 0) return IpScope.None;
        // Loopback handled by caller, but be safe.
        if (a == 127) return IpScope.Loopback;
        // Link-local 169.254/16.
        if (a == 169 && c == 254) return IpScope.LinkLocal;
        // RFC1918 private ranges.
        if (a == 10) return IpScope.Private;
        if (a == 172 && c >= 16 && c <= 31) return IpScope.Private;
        if (a == 192 && c == 168) return IpScope.Private;
        // CGNAT 100.64.0.0/10 — carrier-grade NAT, treat as internal.
        if (a == 100 && c >= 64 && c <= 127) return IpScope.Private;
        // Everything else routable.
        return IpScope.Public;
    }

    private static IpScope ClassifyV6(IPAddress ip)
    {
        if (ip.IsIPv6LinkLocal) return IpScope.LinkLocal;
        var b = ip.GetAddressBytes();
        // Unique-local fc00::/7.
        if ((b[0] & 0xFE) == 0xFC) return IpScope.Private;
        // Unspecified ::.
        if (ip.Equals(IPAddress.IPv6Any)) return IpScope.None;
        return IpScope.Public;
    }

    /// <summary>True when the stored address string is IPv4 (no v4-mapped v6).</summary>
    public static bool IsIPv4(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        return IPAddress.TryParse(address, out var ip) &&
               ip.AddressFamily == AddressFamily.InterNetwork;
    }

    /// <summary>Human-friendly label for a scope.</summary>
    public static string Label(IpScope scope) => scope switch
    {
        IpScope.Private => "Private",
        IpScope.Public => "Internet",
        IpScope.Loopback => "Loopback",
        IpScope.LinkLocal => "Link-local",
        _ => "—"
    };
}
