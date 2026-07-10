using System.Text.RegularExpressions;

namespace ServiceMap.Core.Analysis;

/// <summary>An endpoint (host + optional port) found embedded in a config artifact.</summary>
public sealed class ConfigEndpoint
{
    public string Source { get; set; } = string.Empty;   // file path or registry key
    public string Kind { get; set; } = string.Empty;      // connection-string / url / host / appsetting
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Redacted { get; set; }                 // the surrounding value with secrets masked
    /// <summary>True once reconciled: was this endpoint also seen in observed traffic?</summary>
    public bool ObservedInTraffic { get; set; }
}

/// <summary>
/// Pure text extractor for hardcoded endpoints in config artifacts
/// (app.config / appsettings.json / .env / connection strings). Finds embedded
/// IPs, hostnames, URLs, and DB connection strings; masks secrets. An endpoint
/// found in config but NOT observed in traffic is a latent landmine — the app
/// will try to reach the old address after cutover.
/// </summary>
public static class ConfigScavenger
{
    private static readonly Regex UrlRx = new(
        @"\b([a-z][a-z0-9+.\-]*)://([A-Za-z0-9._\-]+)(?::(\d{1,5}))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DataSourceRx = new(
        @"(?:Data Source|Server|Host|Address|Addr)\s*=\s*([^;""']+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HostPortRx = new(
        @"\b((?:\d{1,3}\.){3}\d{1,3}|[A-Za-z0-9][A-Za-z0-9.\-]{1,60})[:,](\d{1,5})\b", RegexOptions.Compiled);
    private static readonly Regex SecretRx = new(
        @"(?<k>password|pwd|secret|api[_\-]?key|token|accountkey|sharedaccesskey)\s*=\s*[^;,""'\s]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Scan one artifact's text. <paramref name="keepRaw"/> retains full values
    /// (still password-masked); default masks aggressively.
    /// </summary>
    public static List<ConfigEndpoint> Scan(string source, string text, bool keepRaw = false)
    {
        var results = new List<ConfigEndpoint>();
        if (string.IsNullOrEmpty(text)) return results;

        // Connection strings: any line containing a Data Source / Server = ...
        foreach (Match m in DataSourceRx.Matches(text))
        {
            var target = m.Groups[1].Value.Trim();
            var (host, port) = SplitHostPort(target);
            if (host.Length == 0) continue;
            results.Add(new ConfigEndpoint
            {
                Source = source,
                Kind = "connection-string",
                Host = host,
                Port = port,
                Redacted = Redact(LineAround(text, m.Index), keepRaw)
            });
        }

        // URLs (http, tcp, amqp, mongodb, redis, ...).
        foreach (Match m in UrlRx.Matches(text))
        {
            var host = m.Groups[2].Value;
            var port = int.TryParse(m.Groups[3].Value, out var p) ? p : DefaultPort(m.Groups[1].Value);
            if (IsNoise(host)) continue;
            results.Add(new ConfigEndpoint
            {
                Source = source, Kind = "url", Host = host, Port = port,
                Redacted = Redact(m.Value, keepRaw)
            });
        }

        // Bare host:port / ip,port pairs (e.g. appsettings "Endpoint": "10.1.2.3:5672").
        foreach (Match m in HostPortRx.Matches(text))
        {
            var host = m.Groups[1].Value;
            if (IsNoise(host)) continue;
            if (!int.TryParse(m.Groups[2].Value, out var port) || port is < 1 or > 65535) continue;
            results.Add(new ConfigEndpoint
            {
                Source = source, Kind = "host:port", Host = host, Port = port,
                Redacted = Redact(m.Value, keepRaw)
            });
        }

        // Dedupe on (host, port, kind).
        return results
            .GroupBy(e => (e.Host.ToLowerInvariant(), e.Port, e.Kind))
            .Select(g => g.First())
            .OrderBy(e => e.Host).ThenBy(e => e.Port)
            .ToList();
    }

    private static (string Host, int Port) SplitHostPort(string s)
    {
        s = s.Trim().Trim('"', '\'');
        // "tcp:host,1433" (SQL) or "host,1433" or "host:1433" or bare host.
        s = Regex.Replace(s, @"^tcp:", "", RegexOptions.IgnoreCase);
        var m = Regex.Match(s, @"^([^,:]+)[,:](\d{1,5})$");
        if (m.Success && int.TryParse(m.Groups[2].Value, out var p)) return (m.Groups[1].Value, p);
        return (s, 0);
    }

    private static string Redact(string value, bool keepRaw)
    {
        // Passwords/keys are ALWAYS masked, even in keepRaw mode.
        var masked = SecretRx.Replace(value, mm => $"{mm.Groups["k"].Value}=***");
        return keepRaw ? masked : Truncate(masked, 160);
    }

    private static string LineAround(string text, int idx)
    {
        var start = text.LastIndexOf('\n', Math.Min(idx, text.Length - 1));
        var end = text.IndexOf('\n', idx);
        start = start < 0 ? 0 : start + 1;
        end = end < 0 ? text.Length : end;
        return text[start..end].Trim();
    }

    private static bool IsNoise(string host) =>
        host.Length == 0 ||
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host is "127.0.0.1" or "0.0.0.0" or "::1" or "example.com" or "www.w3.org" ||
        host.EndsWith(".example.com", StringComparison.OrdinalIgnoreCase) ||
        host.Contains("schemas.", StringComparison.OrdinalIgnoreCase) ||
        host.All(c => !char.IsLetterOrDigit(c));

    private static int DefaultPort(string scheme) => scheme.ToLowerInvariant() switch
    {
        "http" => 80, "https" => 443, "amqp" => 5672, "amqps" => 5671,
        "redis" => 6379, "mongodb" => 27017, "postgres" or "postgresql" => 5432,
        "mysql" => 3306, "ldap" => 389, "ldaps" => 636, _ => 0
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
