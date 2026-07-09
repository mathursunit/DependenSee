namespace ServiceMap.Remote.Parsing;

/// <summary>Splits "addr:port" endpoints, handling IPv6 brackets and wildcards.</summary>
public static class EndpointParser
{
    /// <summary>Returns (address, port). Port is 0 for wildcard "*" or unparsable.</summary>
    public static (string Addr, int Port) Split(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return (string.Empty, 0);
        endpoint = endpoint.Trim();

        // Bracketed IPv6: [::1]:22 or [fe80::1%eth0]:68
        if (endpoint.StartsWith('['))
        {
            var close = endpoint.IndexOf(']');
            if (close > 0)
            {
                var addr6 = endpoint.Substring(1, close - 1);
                var rest = endpoint[(close + 1)..];
                var port6 = rest.StartsWith(':') ? ParsePort(rest[1..]) : 0;
                return (StripScope(addr6), port6);
            }
        }

        var lastColon = endpoint.LastIndexOf(':');
        if (lastColon < 0) return (endpoint, 0);

        var addr = endpoint[..lastColon];
        var port = ParsePort(endpoint[(lastColon + 1)..]);
        // Wildcard address "*" or "0.0.0.0" stays as-is for the caller to treat.
        return (StripScope(addr), port);
    }

    private static int ParsePort(string s) =>
        int.TryParse(s.Trim(), out var p) ? p : 0;   // "*" -> 0

    private static string StripScope(string addr)
    {
        var pct = addr.IndexOf('%');       // fe80::1%eth0 -> fe80::1
        return pct > 0 ? addr[..pct] : addr;
    }
}
