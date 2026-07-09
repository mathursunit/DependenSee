using ServiceMap.Core.Models;

namespace ServiceMap.Core.Net;

/// <summary>
/// Identifies high-volume, low-value UDP "chatter" that would otherwise bloat
/// the database without adding migration value: service-discovery protocols
/// (SSDP, mDNS, LLMNR, WS-Discovery, NetBIOS), transient ephemeral UDP client
/// sockets (e.g. SSDP search sockets that churn thousands of ports), and
/// multicast/broadcast group traffic. TCP is never filtered.
/// </summary>
public static class NoiseFilter
{
    private static readonly HashSet<int> DiscoveryPorts = new()
    {
        1900,  // SSDP / UPnP
        5353,  // mDNS / Bonjour
        5355,  // LLMNR
        3702,  // WS-Discovery
        137,   // NetBIOS name service
        138    // NetBIOS datagram
    };

    public static bool IsNoise(ConnectionSample s, int ephemeralThreshold = 49152)
    {
        // Only connectionless UDP is ever treated as noise; keep all TCP.
        if (s.Protocol != Protocol.Udp) return false;

        // Well-known discovery / multicast ports.
        if (DiscoveryPorts.Contains(s.LocalPort) || DiscoveryPorts.Contains(s.RemotePort))
            return true;

        // Transient ephemeral UDP client sockets (the bulk of SSDP volume).
        if (s.Direction == ConnectionDirection.Listen && s.LocalPort >= ephemeralThreshold)
            return true;

        // Multicast / broadcast group addresses.
        if (IsMulticastOrBroadcast(s.LocalAddress) || IsMulticastOrBroadcast(s.RemoteAddress))
            return true;

        return false;
    }

    private static bool IsMulticastOrBroadcast(string? addr)
    {
        if (string.IsNullOrEmpty(addr)) return false;
        if (addr == "255.255.255.255") return true;
        // IPv6 multicast is ff00::/8.
        if (addr.StartsWith("ff", StringComparison.OrdinalIgnoreCase)) return true;
        // IPv4 multicast is 224.0.0.0/4 (first octet 224-239).
        var dot = addr.IndexOf('.');
        if (dot > 0 && int.TryParse(addr.AsSpan(0, dot), out var first))
            return first >= 224 && first <= 239;
        return false;
    }
}
