using ServiceMap.Core.Models;

namespace ServiceMap.Core.Analysis;

/// <summary>How a dependency changed between a saved baseline and a later scan.</summary>
public enum FlowChange { Unchanged, Missing, New }

/// <summary>One line of a baseline comparison.</summary>
public sealed class BaselineDiffRow
{
    public FlowChange Change { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string ServiceOrProcess { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public int Port { get; set; }
}

/// <summary>
/// Compares a machine's current distinct flows against a previously saved
/// baseline, turning the tool from planning-only into cutover validation:
///   Missing = in baseline, gone now (a dependency that broke),
///   New     = absent from baseline, present now (unexpected / misconfig),
///   Unchanged = present in both.
/// Ephemeral client ports are already collapsed by the flow aggregation, so the
/// key is direction + protocol + service-side port + peer.
/// </summary>
public static class BaselineComparer
{
    private static (ConnectionDirection, Protocol, int, string) Key(ConnectionAggregate f)
    {
        var port = f.Direction == ConnectionDirection.Outbound ? f.RemotePort : f.LocalPort;
        var peer = f.Direction == ConnectionDirection.Listen ? "" : f.RemoteAddress;
        return (f.Direction, f.Protocol, port, peer);
    }

    public static List<BaselineDiffRow> Compare(
        IReadOnlyList<ConnectionAggregate> baseline,
        IReadOnlyList<ConnectionAggregate> current)
    {
        var baseMap = baseline.ToDictionary(Key, f => f, EqualityComparer<(ConnectionDirection, Protocol, int, string)>.Default);
        var curMap = current.ToDictionary(Key, f => f, EqualityComparer<(ConnectionDirection, Protocol, int, string)>.Default);

        var rows = new List<BaselineDiffRow>();
        foreach (var (k, f) in curMap)
            rows.Add(Row(f, baseMap.ContainsKey(k) ? FlowChange.Unchanged : FlowChange.New));
        foreach (var (k, f) in baseMap)
            if (!curMap.ContainsKey(k))
                rows.Add(Row(f, FlowChange.Missing));

        return rows
            .OrderBy(r => r.Change switch { FlowChange.Missing => 0, FlowChange.New => 1, _ => 2 })
            .ThenBy(r => r.RemoteAddress).ThenBy(r => r.Port)
            .ToList();
    }

    private static BaselineDiffRow Row(ConnectionAggregate f, FlowChange change) => new()
    {
        Change = change,
        Direction = f.Direction.ToString(),
        Protocol = f.Protocol.ToString(),
        ServiceOrProcess = f.ServiceOrProcess,
        RemoteAddress = f.RemoteAddress,
        Port = f.Direction == ConnectionDirection.Outbound ? f.RemotePort : f.LocalPort
    };
}
