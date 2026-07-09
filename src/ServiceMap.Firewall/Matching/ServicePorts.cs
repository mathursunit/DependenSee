using System.Text.RegularExpressions;

namespace ServiceMap.Firewall.Matching;

/// <summary>
/// Maps Palo Alto / Check Point service objects and app-ids to the ports and
/// protocol they imply. Matching is protocol-aware: "service-udp-514" must not
/// cover TCP 514. Unknown names match nothing — earlier digit-harvesting (which
/// turned "SNMPv3" into port 3) produced false coverage.
/// </summary>
public static class ServicePorts
{
    /// <summary>"tcp", "udp", or "any" (protocol not implied by the name).</summary>
    public sealed record ServiceDef(int[] Ports, string Protocol);

    private static readonly Dictionary<string, ServiceDef> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["service-http"] = new(new[] { 80 }, "tcp"),
        ["service-https"] = new(new[] { 443 }, "tcp"),
        ["http"] = new(new[] { 80 }, "tcp"),
        ["https"] = new(new[] { 443 }, "tcp"),
        ["ssl"] = new(new[] { 443 }, "tcp"),
        ["web-browsing"] = new(new[] { 80 }, "tcp"),
        ["smtp"] = new(new[] { 25, 587 }, "tcp"),
        ["service-smtp"] = new(new[] { 25 }, "tcp"),
        ["dns"] = new(new[] { 53 }, "any"),
        ["ntp"] = new(new[] { 123 }, "udp"),
        ["ssh"] = new(new[] { 22 }, "tcp"),
        ["ms-ds-smb"] = new(new[] { 445 }, "tcp"),
        ["smb"] = new(new[] { 445 }, "tcp"),
        ["ldap"] = new(new[] { 389 }, "any"),
        ["ldaps"] = new(new[] { 636 }, "tcp"),
        ["ms-rdp"] = new(new[] { 3389 }, "any"),
        ["rdp"] = new(new[] { 3389 }, "any"),
        ["mysql"] = new(new[] { 3306 }, "tcp"),
        ["ms-sql-m"] = new(new[] { 1433 }, "tcp"),
        ["ms-sql"] = new(new[] { 1433 }, "tcp"),
        ["snmp"] = new(new[] { 161 }, "udp"),
        ["kerberos"] = new(new[] { 88 }, "any"),
        ["ftp"] = new(new[] { 21 }, "tcp"),
        ["telnet"] = new(new[] { 23 }, "tcp"),
        ["syslog"] = new(new[] { 514 }, "any"),
    };

    // Explicit port-in-name shapes only:
    //   tcp-8009, udp-514, service-tcp-8009, service-udp_514,
    //   tcp-8000-8010 (range), service-8080 (protocol unspecified).
    private static readonly Regex ProtoPortRx = new(
        @"^(?:service[-_])?(tcp|udp)[-_](\d{1,5})(?:[-_](\d{1,5}))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BarePortRx = new(
        @"^service[-_](\d{1,5})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// True when the named service/app covers <paramref name="port"/> over
    /// <paramref name="protocol"/> ("tcp"/"udp"). Unknown names match nothing.
    /// </summary>
    public static bool Matches(string name, int port, string protocol)
    {
        name = name.Trim();
        if (Map.TryGetValue(name, out var def))
            return def.Ports.Contains(port) && ProtocolCompatible(def.Protocol, protocol);

        var m = ProtoPortRx.Match(name);
        if (m.Success)
        {
            if (!ProtocolCompatible(m.Groups[1].Value, protocol)) return false;
            if (!int.TryParse(m.Groups[2].Value, out var lo) || lo is < 1 or > 65535) return false;
            if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var hi) && hi is >= 1 and <= 65535)
                return port >= lo && port <= hi;
            return port == lo;
        }

        var b = BarePortRx.Match(name);
        if (b.Success && int.TryParse(b.Groups[1].Value, out var p) && p is >= 1 and <= 65535)
            return port == p;

        return false;
    }

    /// <summary>
    /// Ports implied by a named service/app, protocol-agnostic. Empty when the
    /// name is unknown. Kept for app-id checks where protocol is not encoded.
    /// </summary>
    public static int[] PortsFor(string name)
    {
        name = name.Trim();
        if (Map.TryGetValue(name, out var def)) return def.Ports;
        var m = ProtoPortRx.Match(name);
        if (m.Success && int.TryParse(m.Groups[2].Value, out var lo) && lo is >= 1 and <= 65535)
        {
            if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var hi) &&
                hi is >= 1 and <= 65535 && hi >= lo)
                return Enumerable.Range(lo, hi - lo + 1).ToArray();
            return new[] { lo };
        }
        var b = BarePortRx.Match(name);
        if (b.Success && int.TryParse(b.Groups[1].Value, out var p) && p is >= 1 and <= 65535)
            return new[] { p };
        return Array.Empty<int>();
    }

    private static bool ProtocolCompatible(string ruleProto, string flowProto) =>
        ruleProto.Equals("any", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrEmpty(flowProto) ||
        ruleProto.Equals(flowProto, StringComparison.OrdinalIgnoreCase);

    public static bool IsAny(string name) =>
        name.Equals("any", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("application-default", StringComparison.OrdinalIgnoreCase);
}
