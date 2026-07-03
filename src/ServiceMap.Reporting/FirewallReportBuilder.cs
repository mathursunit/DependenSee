using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ServiceMap.Core.Models;
using ServiceMap.Core.Net;
using ServiceMap.Core.Storage;

namespace ServiceMap.Reporting;

/// <summary>
/// Turns stored connection samples into a firewall-oriented report: inbound
/// allow-rules (who may reach our service ports), outbound allow-rules (what we
/// reach), and open ports with no observed traffic.
/// </summary>
public static class FirewallReportBuilder
{
    public static FirewallReport Build(SampleRepository repo, ConnectionQuery window, FirewallReportOptions options)
    {
        // Always aggregate to distinct flows across the whole window.
        var query = Clone(window);
        query.Limit = 1_000_000;
        var flows = repo.QueryUniqueConnections(query);

        var report = new FirewallReport
        {
            MachineName = Environment.MachineName,
            FilterSummary = options.FilterSummary,
            LogoPath = options.LogoPath,
            GeneratedUtc = DateTime.UtcNow
        };
        foreach (var ip in LocalIpv4Addresses()) report.MachineAddresses.Add(ip);

        if (flows.Count > 0)
        {
            report.WindowStart = flows.Min(f => f.FirstSeen);
            report.WindowEnd = flows.Max(f => f.LastSeen);
        }

        BuildInbound(report, flows, options);
        BuildOutbound(report, flows);
        Enrich(report, options.Annotations);

        if (options.ResolveHostnames)
            ResolveHosts(report);

        return report;
    }

    private static void Enrich(FirewallReport report, IReadOnlyDictionary<string, Annotation>? ann)
    {
        if (ann is null) return;
        foreach (var r in report.Inbound)
            r.Note = NoteFor(ann, AnnotationKind.Process, r.Process)
                  ?? NoteFor(ann, AnnotationKind.Port, r.LocalPort.ToString());
        foreach (var o in report.Outbound)
            o.Note = NoteFor(ann, AnnotationKind.Host, o.RemoteAddress)
                  ?? NoteFor(ann, AnnotationKind.Process, o.Process);
    }

    private static string? NoteFor(IReadOnlyDictionary<string, Annotation> ann, AnnotationKind kind, string key)
    {
        if (!ann.TryGetValue($"{(int)kind}:{key}", out var a)) return null;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(a.FriendlyName)) parts.Add(a.FriendlyName!);
        if (!string.IsNullOrWhiteSpace(a.Owner)) parts.Add("owner: " + a.Owner);
        if (a.Criticality != Criticality.Unset) parts.Add(a.Criticality.ToString().ToLowerInvariant() + " criticality");
        return parts.Count > 0 ? string.Join(" \u00b7 ", parts) : null;
    }

    private static void BuildInbound(FirewallReport report, IReadOnlyList<ConnectionAggregate> flows, FirewallReportOptions options)
    {
        var rules = new Dictionary<(Protocol, int, string), InboundRule>();

        InboundRule Get(Protocol proto, int port, string process)
        {
            var key = (proto, port, process);
            if (!rules.TryGetValue(key, out var rule))
            {
                rule = new InboundRule { Protocol = proto, LocalPort = port, Process = process };
                rules[key] = rule;
            }
            return rule;
        }

        // Inbound flows contribute the actual sources.
        foreach (var f in flows.Where(f => f.Direction == ConnectionDirection.Inbound))
        {
            var rule = Get(f.Protocol, f.LocalPort, f.ServiceOrProcess);
            rule.ObservedTraffic = true;
            rule.Occurrences += f.SampleCount;
            Accumulate(rule, f.FirstSeen, f.LastSeen);
            if (!string.IsNullOrEmpty(f.RemoteAddress) &&
                !rule.Sources.Any(s => s.Address == f.RemoteAddress))
                rule.Sources.Add(new PeerRef { Address = f.RemoteAddress, Scope = f.RemoteScope });
        }

        // Listeners ensure open ports appear even with no observed inbound.
        foreach (var f in flows.Where(f => f.Direction == ConnectionDirection.Listen))
        {
            var rule = Get(f.Protocol, f.LocalPort, f.ServiceOrProcess);
            Accumulate(rule, f.FirstSeen, f.LastSeen);
        }

        if (options.SummarizeSourcesToCidr)
            foreach (var rule in rules.Values)
                SummarizeSources(rule);

        report.Inbound.AddRange(rules.Values
            .OrderBy(r => r.LocalPort).ThenBy(r => r.Protocol));
    }

    private static void BuildOutbound(FirewallReport report, IReadOnlyList<ConnectionAggregate> flows)
    {
        foreach (var f in flows.Where(f => f.Direction == ConnectionDirection.Outbound))
        {
            report.Outbound.Add(new OutboundRule
            {
                Protocol = f.Protocol,
                RemoteAddress = f.RemoteAddress,
                RemotePort = f.RemotePort,
                Process = f.ServiceOrProcess,
                Scope = f.RemoteScope,
                FirstSeen = f.FirstSeen,
                LastSeen = f.LastSeen,
                Occurrences = f.SampleCount
            });
        }
        // Internet destinations first (they need the most scrutiny), then by address.
        report.Outbound.Sort((a, b) =>
        {
            int byScope = (b.Scope == IpScope.Public).CompareTo(a.Scope == IpScope.Public);
            if (byScope != 0) return byScope;
            int byAddr = string.CompareOrdinal(a.RemoteAddress, b.RemoteAddress);
            return byAddr != 0 ? byAddr : a.RemotePort.CompareTo(b.RemotePort);
        });
    }

    private static void SummarizeSources(InboundRule rule)
    {
        var grouped = rule.Sources
            .Where(s => IpClassifier.IsIPv4(s.Address))
            .GroupBy(s => Slash24(s.Address))
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var g in grouped)
        {
            // Remove the individual hosts and replace with a single /24 entry.
            rule.Sources.RemoveAll(s => IpClassifier.IsIPv4(s.Address) && Slash24(s.Address) == g.Key);
            rule.Sources.Add(new PeerRef
            {
                Address = g.Key + "/24",
                Scope = g.First().Scope,
                Host = g.Count() + " hosts"
            });
        }
    }

    private static string Slash24(string addr)
    {
        var p = addr.Split('.');
        return p.Length == 4 ? $"{p[0]}.{p[1]}.{p[2]}.0" : addr;
    }

    private static void Accumulate(InboundRule rule, DateTime first, DateTime last)
    {
        if (rule.FirstSeen == default || first < rule.FirstSeen) rule.FirstSeen = first;
        if (last > rule.LastSeen) rule.LastSeen = last;
    }

    private static void ResolveHosts(FirewallReport report)
    {
        var addrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in report.Inbound)
            foreach (var s in r.Sources)
                if (IPAddress.TryParse(s.Address, out _)) addrs.Add(s.Address);
        foreach (var o in report.Outbound)
            if (IPAddress.TryParse(o.RemoteAddress, out _)) addrs.Add(o.RemoteAddress);

        // Cap the work and resolve in parallel with a short per-lookup timeout.
        var map = new System.Collections.Concurrent.ConcurrentDictionary<string, string?>();
        Parallel.ForEach(addrs.Take(300),
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            addr => map[addr] = ReverseDns(addr));

        foreach (var r in report.Inbound)
            foreach (var s in r.Sources)
                if (map.TryGetValue(s.Address, out var h)) s.Host = h;
        foreach (var o in report.Outbound)
            if (map.TryGetValue(o.RemoteAddress, out var h)) o.RemoteHost = h;
    }

    private static string? ReverseDns(string addr)
    {
        try
        {
            var task = Dns.GetHostEntryAsync(addr);
            return task.Wait(TimeSpan.FromMilliseconds(1200)) ? task.Result.HostName : null;
        }
        catch { return null; }
    }

    private static IEnumerable<string> LocalIpv4Addresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ua.Address))
                        result.Add(ua.Address.ToString());
                }
            }
        }
        catch { /* best effort */ }
        return result.Distinct();
    }

    private static ConnectionQuery Clone(ConnectionQuery q) => new()
    {
        From = q.From,
        To = q.To,
        ProcessName = q.ProcessName,
        LocalPort = q.LocalPort,
        RemoteAddress = q.RemoteAddress,
        Protocol = q.Protocol,
        Direction = q.Direction,
        AddressFamily = q.AddressFamily,
        Scope = q.Scope,
        Limit = q.Limit
    };
}
