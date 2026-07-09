using System.Text.RegularExpressions;
using ServiceMap.Core.Models;

namespace ServiceMap.Remote.Parsing;

/// <summary>
/// Parses the combined output of a single remote shell command that emits, in
/// marker-delimited sections: the hostname, `ss -H -tunap`, and two systemctl
/// listings. All parsing is pure text so it can be unit-tested offline.
/// </summary>
public static class SshOutputParser
{
    public const string HostMarker  = "###HOST";
    public const string SsMarker    = "###SS";
    public const string UnitsMarker = "###UNITS";
    public const string FilesMarker = "###FILES";
    public const string CtMarker    = "###CT";

    /// <summary>The one command whose output <see cref="Parse"/> consumes.</summary>
    public const string Command =
        // Non-interactive SSH sessions often have a minimal PATH that omits
        // /usr/sbin and /sbin, where ss and systemctl live — add them explicitly.
        "export PATH=\"$PATH:/usr/sbin:/sbin:/usr/local/sbin:/usr/bin:/bin\"; " +
        "echo '" + HostMarker + "'; (hostname 2>/dev/null || cat /proc/sys/kernel/hostname 2>/dev/null); " +
        "echo '" + SsMarker + "'; ss -H -tunap 2>/dev/null; " +
        "echo '" + UnitsMarker + "'; systemctl list-units --type=service --all --no-legend --no-pager --plain 2>/dev/null; " +
        "echo '" + FilesMarker + "'; systemctl list-unit-files --type=service --no-legend --no-pager --plain 2>/dev/null; " +
        // Kernel connection tracking retains recently-closed flows that an ss
        // snapshot misses (needs root and a loaded conntrack module; both
        // fallbacks fail silently on hosts without it).
        "echo '" + CtMarker + "'; (conntrack -L 2>/dev/null || cat /proc/net/nf_conntrack 2>/dev/null) | head -20000";

    private static readonly Regex PidRx  = new(@"pid=(\d+)", RegexOptions.Compiled);
    private static readonly Regex NameRx = new("\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex CtTupleRx = new(
        @"src=(\S+)\s+dst=(\S+)\s+sport=(\d+)\s+dport=(\d+)", RegexOptions.Compiled);

    public sealed class Parsed
    {
        public string MachineName = string.Empty;
        public List<ServiceRecord> Services = new();
        public List<ConnectionSample> Connections = new();
    }

    public static Parsed Parse(string output, DateTime timestampUtc)
    {
        var sections = Split(output);
        var result = new Parsed
        {
            MachineName = sections.GetValueOrDefault(HostMarker, string.Empty)
                .Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty
        };

        foreach (var line in Lines(sections.GetValueOrDefault(SsMarker, string.Empty)))
        {
            var s = ParseSsLine(line, timestampUtc);
            if (s is not null) result.Connections.Add(s);
        }
        DirectionResolver.Assign(result.Connections);

        // Conntrack rows are appended AFTER direction assignment: their
        // direction comes from the originator tuple, not the listen heuristic.
        ParseConntrack(sections.GetValueOrDefault(CtMarker, string.Empty),
            result.Connections, timestampUtc);

        result.Services = ParseServices(
            sections.GetValueOrDefault(UnitsMarker, string.Empty),
            sections.GetValueOrDefault(FilesMarker, string.Empty),
            timestampUtc);

        return result;
    }

    private static Dictionary<string, string> Split(string output)
    {
        var map = new Dictionary<string, string>();
        string? current = null;
        var sb = new System.Text.StringBuilder();
        foreach (var raw in output.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw;
            if (line.StartsWith("###"))
            {
                if (current is not null) map[current] = sb.ToString();
                current = line.Trim();
                sb.Clear();
            }
            else if (current is not null)
            {
                sb.Append(line).Append('\n');
            }
        }
        if (current is not null) map[current] = sb.ToString();
        return map;
    }

    private static IEnumerable<string> Lines(string block) =>
        block.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    private static ConnectionSample? ParseSsLine(string line, DateTime ts)
    {
        var tok = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length < 5) return null;

        var netid = tok[0].ToLowerInvariant();
        var proto = netid.StartsWith("tcp") ? Protocol.Tcp
                  : netid.StartsWith("udp") ? Protocol.Udp
                  : (Protocol?)null;
        if (proto is null) return null;

        var stateTok = tok[1].ToUpperInvariant();
        // ss columns: Netid State Recv-Q Send-Q Local Peer [Process]
        var local = tok.Length > 4 ? tok[4] : string.Empty;
        var peer  = tok.Length > 5 ? tok[5] : string.Empty;
        var procField = tok.Length > 6 ? string.Join(' ', tok[6..]) : string.Empty;

        var (laddr, lport) = EndpointParser.Split(local);
        var (raddr, rport) = EndpointParser.Split(peer);

        var state = stateTok switch
        {
            "LISTEN" => TcpState.Listen,
            "ESTAB" => TcpState.Established,
            "TIME-WAIT" => TcpState.TimeWait,
            "CLOSE-WAIT" => TcpState.CloseWait,
            "SYN-SENT" => TcpState.SynSent,
            "SYN-RECV" => TcpState.SynReceived,
            "FIN-WAIT-1" => TcpState.FinWait1,
            "FIN-WAIT-2" => TcpState.FinWait2,
            "LAST-ACK" => TcpState.LastAck,
            "CLOSING" => TcpState.Closing,
            _ => TcpState.Unknown   // UNCONN (udp), etc.
        };

        // Wildcard peer means a listener/unconnected socket -> no remote endpoint.
        var wildcardPeer = raddr is "*" or "0.0.0.0" or "::" || rport == 0;
        var sample = new ConnectionSample
        {
            Protocol = proto.Value,
            LocalAddress = laddr,
            LocalPort = lport,
            RemoteAddress = wildcardPeer ? string.Empty : raddr,
            RemotePort = wildcardPeer ? 0 : rport,
            State = state,
            Timestamp = ts
        };

        var pidM = PidRx.Match(procField);
        if (pidM.Success && int.TryParse(pidM.Groups[1].Value, out var pid)) sample.ProcessId = pid;
        var nameM = NameRx.Match(procField);
        if (nameM.Success) sample.ProcessName = nameM.Groups[1].Value;

        return sample;
    }

    /// <summary>
    /// Fold kernel connection-tracking entries into the sample list. An ss
    /// snapshot only sees sockets open at that instant; conntrack retains
    /// recently-closed flows (TIME_WAIT and tracked-but-closed entries), which
    /// is the closest agentless equivalent to event capture. Rows are kept only
    /// when one side matches an address this host was seen using, direction is
    /// derived from the originator tuple (src local = outbound), and flows the
    /// ss sweep already reported are skipped. No PID attribution is available.
    /// Handles both `conntrack -L` and /proc/net/nf_conntrack formats.
    /// </summary>
    private static void ParseConntrack(string block, List<ConnectionSample> connections, DateTime ts)
    {
        if (string.IsNullOrWhiteSpace(block)) return;

        var localAddrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connections)
            if (c.LocalAddress is not ("" or "*" or "0.0.0.0" or "::"))
                localAddrs.Add(c.LocalAddress);
        if (localAddrs.Count == 0) return;

        var seen = new HashSet<(Protocol, string, int, string, int)>();
        foreach (var c in connections)
            seen.Add((c.Protocol, c.LocalAddress, c.LocalPort, c.RemoteAddress, c.RemotePort));

        foreach (var line in Lines(block))
        {
            var proto = line.Contains("tcp") ? Protocol.Tcp
                      : line.Contains("udp") ? Protocol.Udp
                      : (Protocol?)null;
            if (proto is null) continue;

            // First src/dst tuple is the originator's view of the flow.
            var m = CtTupleRx.Match(line);
            if (!m.Success) continue;
            var src = m.Groups[1].Value;
            var dst = m.Groups[2].Value;
            if (!int.TryParse(m.Groups[3].Value, out var sport)) continue;
            if (!int.TryParse(m.Groups[4].Value, out var dport)) continue;

            var srcLocal = localAddrs.Contains(src);
            var dstLocal = localAddrs.Contains(dst);
            // Neither side is this host (forwarded/NATed traffic on a router,
            // or an address we never saw) - not a dependency of this machine.
            if (!srcLocal && !dstLocal) continue;

            var outbound = srcLocal;
            var sample = new ConnectionSample
            {
                Protocol = proto.Value,
                LocalAddress = outbound ? src : dst,
                LocalPort = outbound ? sport : dport,
                RemoteAddress = outbound ? dst : src,
                RemotePort = outbound ? dport : sport,
                State = proto == Protocol.Tcp ? MapCtState(line) : TcpState.Unknown,
                Direction = outbound ? ConnectionDirection.Outbound : ConnectionDirection.Inbound,
                Timestamp = ts
            };

            if (!seen.Add((sample.Protocol, sample.LocalAddress, sample.LocalPort,
                           sample.RemoteAddress, sample.RemotePort)))
                continue;   // ss already reported this exact flow

            connections.Add(sample);
        }
    }

    private static TcpState MapCtState(string line)
    {
        if (line.Contains("ESTABLISHED")) return TcpState.Established;
        if (line.Contains("TIME_WAIT")) return TcpState.TimeWait;
        if (line.Contains("CLOSE_WAIT")) return TcpState.CloseWait;
        if (line.Contains("SYN_SENT")) return TcpState.SynSent;
        if (line.Contains("SYN_RECV")) return TcpState.SynReceived;
        if (line.Contains("FIN_WAIT")) return TcpState.FinWait1;
        if (line.Contains("LAST_ACK")) return TcpState.LastAck;
        if (line.Contains("CLOSE")) return TcpState.Closed;
        return TcpState.Unknown;
    }

    private static List<ServiceRecord> ParseServices(string units, string files, DateTime ts)
    {
        // Enabled/disabled state from list-unit-files, keyed by unit name.
        var startMode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines(files))
        {
            var tok = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length >= 2) startMode[UnitName(tok[0])] = tok[1];
        }

        var list = new List<ServiceRecord>();
        foreach (var line in Lines(units))
        {
            var tok = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length < 4) continue;
            var name = UnitName(tok[0]);
            list.Add(new ServiceRecord
            {
                Name = name,
                DisplayName = tok.Length > 4 ? string.Join(' ', tok[4..]) : name,
                State = tok[3],                       // sub-state: running/exited/dead
                StartMode = startMode.GetValueOrDefault(name, tok[2]),  // enabled/disabled or active
                ScanTimestamp = ts
            });
        }
        return list;
    }

    private static string UnitName(string unit) =>
        unit.EndsWith(".service", StringComparison.OrdinalIgnoreCase) ? unit[..^8] : unit;
}
