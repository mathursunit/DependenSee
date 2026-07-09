using System.Net;

namespace ServiceMap.Remote;

/// <summary>
/// Expands a free-form target specification into concrete hosts. Accepts any mix
/// (comma / semicolon / newline separated) of: single hostnames or IPs, CIDR
/// blocks (10.0.0.0/24), and dashed ranges (10.0.0.1-10.0.0.50 or 10.0.0.1-50).
/// </summary>
public static class TargetExpander
{
    /// <summary>Safety cap so a wide CIDR can't expand into an unbounded scan.</summary>
    public const int MaxHosts = 4096;

    public static IReadOnlyList<string> Expand(string spec)
    {
        var hosts = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(spec)) return hosts;

        foreach (var raw in spec.Split(new[] { ',', ';', '\n', '\r', ' ', '\t' },
                                       StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;

            IEnumerable<string> expanded =
                token.Contains('/') ? ExpandCidr(token) :
                token.Contains('-') && LooksLikeRange(token) ? ExpandRange(token) :
                new[] { token };

            foreach (var h in expanded)
            {
                if (hosts.Count >= MaxHosts) return hosts;
                if (seen.Add(h)) hosts.Add(h);
            }
        }
        return hosts;
    }

    private static bool LooksLikeRange(string token)
    {
        // Avoid treating hostnames with dashes (my-server) as ranges: require the
        // part before '-' to be an IPv4 literal.
        var dash = token.IndexOf('-');
        return IPAddress.TryParse(token[..dash], out var ip) &&
               ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    private static IEnumerable<string> ExpandRange(string token)
    {
        var dash = token.IndexOf('-');
        var startStr = token[..dash].Trim();
        var endStr = token[(dash + 1)..].Trim();
        if (!IPAddress.TryParse(startStr, out var start)) yield break;

        var sb = start.GetAddressBytes();
        uint startVal = ToUInt(sb);
        uint endVal;
        if (endStr.Contains('.') && IPAddress.TryParse(endStr, out var end))
            endVal = ToUInt(end.GetAddressBytes());
        else if (int.TryParse(endStr, out var lastOctet) && lastOctet is >= 0 and <= 255)
            endVal = (startVal & 0xFFFFFF00u) | (uint)lastOctet;
        else
            yield break;

        if (endVal < startVal) (startVal, endVal) = (endVal, startVal);
        for (uint v = startVal; v <= endVal; v++)
        {
            yield return FromUInt(v);
            if (v - startVal + 1 >= MaxHosts) yield break;
        }
    }

    private static IEnumerable<string> ExpandCidr(string token)
    {
        var slash = token.IndexOf('/');
        if (!IPAddress.TryParse(token[..slash], out var baseIp) ||
            baseIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !int.TryParse(token[(slash + 1)..], out var prefix) || prefix is < 0 or > 32)
            yield break;

        uint mask = prefix == 0 ? 0 : 0xFFFFFFFFu << (32 - prefix);
        uint network = ToUInt(baseIp.GetAddressBytes()) & mask;
        uint broadcast = network | ~mask;

        // For /31 and /32, include every address; otherwise skip network+broadcast.
        uint first = prefix >= 31 ? network : network + 1;
        uint last = prefix >= 31 ? broadcast : (broadcast == 0 ? 0 : broadcast - 1);

        int count = 0;
        for (uint v = first; v <= last; v++)
        {
            yield return FromUInt(v);
            if (++count >= MaxHosts) yield break;
        }
    }

    private static uint ToUInt(byte[] b) => ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    private static string FromUInt(uint v) => $"{(v >> 24) & 0xFF}.{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}";
}
