using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.App.Controls;
using ServiceMap.App.Services;
using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;

namespace ServiceMap.App.ViewModels;

/// <summary>
/// Builds the dependency-map graph — either this machine and its peers, or the
/// imported fleet as machine-to-machine (and machine-to-internet) nodes.
/// </summary>
public sealed partial class MapViewModel : ViewModelBase
{
    private readonly DataAccess _data;
    private readonly MultiSourceDataAccess _multi;

    public string[] ModeOptions { get; } = { "This machine", "Fleet" };
    [ObservableProperty] private string _selectedMode = "This machine";
    [ObservableProperty] private int _hoursBack = 168;
    [ObservableProperty] private int _maxPeers = 40;
    [ObservableProperty] private string _status = "Choose a mode and click Refresh.";

    [ObservableProperty] private IReadOnlyList<GraphNode> _nodes = new List<GraphNode>();
    [ObservableProperty] private IReadOnlyList<GraphEdge> _edges = new List<GraphEdge>();

    private static readonly Color CenterFill = Color.FromRgb(0x1F, 0x29, 0x37);
    private static readonly Color PrivateFill = Color.FromRgb(0x9C, 0xA3, 0xAF);
    private static readonly Color InternetFill = Color.FromRgb(0xB4, 0x23, 0x18);
    private static readonly Color LocalFill = Color.FromRgb(0xD1, 0xD5, 0xDB);
    private static readonly Color MachineFill = Color.FromRgb(0x25, 0x63, 0xEB);
    private static readonly Color OutEdge = Color.FromArgb(150, 0x25, 0x63, 0xEB);
    private static readonly Color InEdge = Color.FromArgb(150, 0x05, 0x96, 0x69);
    private static readonly Color CrossEdge = Color.FromArgb(180, 0xD9, 0x77, 0x06);
    private static readonly Color NetEdge = Color.FromArgb(150, 0xB4, 0x23, 0x18);

    public MapViewModel(DataAccess data, MultiSourceDataAccess multi)
    {
        _data = data;
        _multi = multi;
    }

    [RelayCommand]
    private void Refresh()
    {
        if (SelectedMode == "Fleet") BuildFleet();
        else BuildSingle();
    }

    private ConnectionQuery Query() => new()
    {
        From = System.DateTime.UtcNow.AddHours(-System.Math.Max(1, HoursBack)),
        AddressFamily = AddressFamilyOption.IPv4,
        Limit = 1_000_000
    };

    private void BuildSingle()
    {
        var flows = _data.QueryUnique(Query())
            .Where(f => f.Direction != ConnectionDirection.Listen && !string.IsNullOrEmpty(f.RemoteAddress))
            .ToList();

        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();

        var centerName = _data.GetMachineName() ?? "This machine";
        nodes.Add(new GraphNode { Id = "_self", Label = centerName, X = 0.5, Y = 0.5, Radius = 34, Fill = CenterFill, Stroke = CenterFill });

        var peers = flows.GroupBy(f => f.RemoteAddress)
            .Select(g => new
            {
                Addr = g.Key,
                Scope = g.First().RemoteScope,
                Count = g.Sum(x => x.SampleCount),
                HasOut = g.Any(x => x.Direction == ConnectionDirection.Outbound),
                Port = g.OrderByDescending(x => x.SampleCount).First().RemotePort
            })
            .OrderByDescending(p => p.Count)
            .Take(System.Math.Max(1, MaxPeers))
            .ToList();

        int n = peers.Count;
        for (int i = 0; i < n; i++)
        {
            var p = peers[i];
            double ang = 2 * System.Math.PI * i / System.Math.Max(1, n);
            double x = 0.5 + 0.42 * System.Math.Cos(ang);
            double y = 0.5 + 0.42 * System.Math.Sin(ang);
            var id = "p" + i;
            nodes.Add(new GraphNode
            {
                Id = id,
                Label = p.Addr,
                SubLabel = p.Port > 0 ? ":" + p.Port : null,
                X = x, Y = y, Radius = 8 + System.Math.Min(16, p.Count / 50.0),
                Fill = FillForScope(p.Scope),
                Stroke = Color.FromRgb(0x6B, 0x72, 0x80)
            });
            edges.Add(new GraphEdge { FromId = "_self", ToId = id, Color = p.HasOut ? OutEdge : InEdge, Width = 1.5 });
        }

        Nodes = nodes; Edges = edges;
        Status = $"This machine + {n} peers (top {MaxPeers} by volume, last {HoursBack}h).";
    }

    private void BuildFleet()
    {
        var machines = _multi.GetMachines();
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();

        if (machines.Count == 0)
        {
            Nodes = nodes; Edges = edges;
            Status = "Import machines on the Fleet tab first.";
            return;
        }

        var idByMachine = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        int n = machines.Count;
        for (int i = 0; i < n; i++)
        {
            double ang = 2 * System.Math.PI * i / n;
            double x = 0.5 + 0.36 * System.Math.Cos(ang);
            double y = 0.5 + 0.36 * System.Math.Sin(ang);
            var id = "m" + i;
            idByMachine[machines[i].Name] = id;
            nodes.Add(new GraphNode
            {
                Id = id,
                Label = machines[i].Name,
                SubLabel = string.IsNullOrEmpty(machines[i].Wave) ? null : "wave " + machines[i].Wave,
                X = x, Y = y, Radius = 26, Fill = MachineFill, Stroke = CenterFill
            });
        }

        // Machine-to-machine dependencies.
        foreach (var d in _multi.DetectCrossDependencies(Query()))
        {
            if (idByMachine.TryGetValue(d.FromMachine, out var from) &&
                idByMachine.TryGetValue(d.ToMachine, out var to))
            {
                var color = d.CrossesWaveBoundary && !string.IsNullOrEmpty(d.FromWave) && !string.IsNullOrEmpty(d.ToWave)
                    ? InternetFill : CrossEdge;
                edges.Add(new GraphEdge { FromId = from, ToId = to, Color = Color.FromArgb(200, color.R, color.G, color.B), Width = 2 });
            }
        }

        // Machine-to-internet (any public outbound).
        var flows = _multi.QueryUniqueAll(Query());
        bool anyInternet = flows.Any(f => f.Direction == ConnectionDirection.Outbound && f.RemoteScope == IpScope.Public);
        if (anyInternet)
        {
            nodes.Add(new GraphNode { Id = "_net", Label = "Internet", X = 0.5, Y = 0.5, Radius = 22, Fill = InternetFill, Stroke = CenterFill });
            foreach (var g in flows.Where(f => f.Direction == ConnectionDirection.Outbound && f.RemoteScope == IpScope.Public)
                                    .GroupBy(f => f.Machine))
            {
                if (idByMachine.TryGetValue(g.Key, out var mid))
                    edges.Add(new GraphEdge { FromId = mid, ToId = "_net", Color = NetEdge, Width = 1.5 });
            }
        }

        Nodes = nodes; Edges = edges;
        Status = $"{machines.Count} machines · {edges.Count} links (last {HoursBack}h).";
    }

    private static Color FillForScope(IpScope scope) => scope switch
    {
        IpScope.Public => InternetFill,
        IpScope.Private => PrivateFill,
        _ => LocalFill
    };
}
