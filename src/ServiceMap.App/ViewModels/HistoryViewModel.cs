using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.App.Services;
using ServiceMap.Core.Export;
using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;
using ServiceMap.Reporting;

namespace ServiceMap.App.ViewModels;

/// <summary>Filterable history query over stored connection samples, plus export.</summary>
public sealed partial class HistoryViewModel : ViewModelBase
{
    private readonly DataAccess _data;
    private readonly Func<string> _exportDirectory;
    private readonly DispatcherTimer _debounce;
    private bool _ready;

    /// <summary>Native Save-As dialog; set after the window exists.</summary>
    public IFileSaveService? SaveService { get; set; }

    /// <summary>Annotations lookup used to enrich the firewall report.</summary>
    public Func<IReadOnlyDictionary<string, Annotation>>? AnnotationsProvider { get; set; }

    public ObservableCollection<ConnectionSample> Results { get; } = new();
    public ObservableCollection<ConnectionAggregate> UniqueResults { get; } = new();

    // Raw (pre column-filter) result sets, kept so Excel-style column filters can
    // re-filter instantly without hitting the database again.
    private List<ConnectionSample> _rawResults = new();
    private List<ConnectionAggregate> _rawUnique = new();

    // Excel-style per-column checklist filters (shared across raw + unique grids).
    public ColumnFilter ProtocolCol { get; }
    public ColumnFilter DirectionCol { get; }
    public ColumnFilter ScopeCol { get; }
    public ColumnFilter ServiceCol { get; }
    public ColumnFilter ProcessCol { get; }
    public ColumnFilter LocalAddrCol { get; }
    public ColumnFilter RemoteAddrCol { get; }
    public ColumnFilter LocalPortCol { get; }
    public ColumnFilter RemotePortCol { get; }
    private readonly ColumnFilter[] _columnFilters;

    private static Func<object, string> Sel(Func<ConnectionSample, object?> s, Func<ConnectionAggregate, object?> a)
        => o => o switch
        {
            ConnectionSample cs => s(cs)?.ToString() ?? string.Empty,
            ConnectionAggregate ca => a(ca)?.ToString() ?? string.Empty,
            _ => string.Empty
        };

    [ObservableProperty] private string? _processNameFilter;
    [ObservableProperty] private string? _remoteAddressFilter;
    [ObservableProperty] private string? _localPortFilter;
    [ObservableProperty] private int _hoursBack = 24;

    public string[] ProtocolOptions { get; } = { "Any", "Tcp", "Udp" };
    [ObservableProperty] private string _selectedProtocol = "Any";

    public string[] DirectionOptions { get; } = { "Any", "Listen", "Inbound", "Outbound" };
    [ObservableProperty] private string _selectedDirection = "Any";

    public string[] FamilyOptions { get; } = { "Any", "IPv4", "IPv6" };
    [ObservableProperty] private string _selectedFamily = "IPv4";

    public string[] ScopeOptions { get; } = { "Any", "Private", "Internet", "Loopback", "Link-local" };
    [ObservableProperty] private string _selectedScope = "Any";

    [ObservableProperty] private string? _localAddressFilter;
    [ObservableProperty] private string? _localNotContains;
    [ObservableProperty] private string? _localPortNotFilter;
    [ObservableProperty] private string? _processNotContains;
    [ObservableProperty] private string? _remoteNotContains;
    [ObservableProperty] private bool _excludeEphemeral;
    [ObservableProperty] private bool _hideSameSubnet;

    [ObservableProperty] private bool _uniqueOnly;
    [ObservableProperty] private bool _resolveHostnames = true;
    [ObservableProperty] private bool _summarizeSources;

    [ObservableProperty] private int _limit = 5000;
    [ObservableProperty] private string _resultSummary = "Adjust a filter to run a query.";

    public bool ShowRaw => !UniqueOnly;
    public bool ShowUnique => UniqueOnly;

    public HistoryViewModel(DataAccess data, Func<string> exportDirectory)
    {
        _data = data;
        _exportDirectory = exportDirectory;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunQuery(); };
        ProtocolCol   = new ColumnFilter("Proto",          Sel(x => x.Protocol,       x => x.Protocol));
        DirectionCol  = new ColumnFilter("Direction",      Sel(x => x.Direction,      x => x.Direction));
        ScopeCol      = new ColumnFilter("Scope",          Sel(x => x.RemoteScope,    x => x.RemoteScope));
        ServiceCol    = new ColumnFilter("Service",        Sel(x => x.ServiceOrProcess, x => x.ServiceOrProcess));
        ProcessCol    = new ColumnFilter("Process",        Sel(x => x.ProcessName,    x => x.ProcessName));
        LocalAddrCol  = new ColumnFilter("Local address",  Sel(x => x.LocalAddress,   x => x.LocalAddress));
        RemoteAddrCol = new ColumnFilter("Remote address", Sel(x => x.RemoteAddress,  x => x.RemoteAddress));
        LocalPortCol  = new ColumnFilter("L.Port",         Sel(x => x.LocalPort,      x => x.LocalPort));
        RemotePortCol = new ColumnFilter("R.Port",         Sel(x => x.RemotePort,     x => x.RemotePort));
        _columnFilters = new[]
        {
            ProtocolCol, DirectionCol, ScopeCol, ServiceCol, ProcessCol,
            LocalAddrCol, RemoteAddrCol, LocalPortCol, RemotePortCol
        };
        foreach (var f in _columnFilters) f.Bind(ApplyColumnFilters);

        _ready = true;
    }

    // Any filter change schedules a debounced auto-run.
    partial void OnProcessNameFilterChanged(string? value) => ScheduleQuery();
    partial void OnRemoteAddressFilterChanged(string? value) => ScheduleQuery();
    partial void OnLocalPortFilterChanged(string? value) => ScheduleQuery();
    partial void OnHoursBackChanged(int value) => ScheduleQuery();
    partial void OnSelectedProtocolChanged(string value) => ScheduleQuery();
    partial void OnSelectedDirectionChanged(string value) => ScheduleQuery();
    partial void OnSelectedFamilyChanged(string value) => ScheduleQuery();
    partial void OnSelectedScopeChanged(string value) => ScheduleQuery();
    partial void OnLimitChanged(int value) => ScheduleQuery();
    partial void OnLocalAddressFilterChanged(string? value) => ScheduleQuery();
    partial void OnLocalNotContainsChanged(string? value) => ScheduleQuery();
    partial void OnLocalPortNotFilterChanged(string? value) => ScheduleQuery();
    partial void OnProcessNotContainsChanged(string? value) => ScheduleQuery();
    partial void OnRemoteNotContainsChanged(string? value) => ScheduleQuery();
    partial void OnExcludeEphemeralChanged(bool value) => ScheduleQuery();
    partial void OnHideSameSubnetChanged(bool value) => ScheduleQuery();

    partial void OnUniqueOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRaw));
        OnPropertyChanged(nameof(ShowUnique));
        ScheduleQuery();
    }

    private void ScheduleQuery()
    {
        if (!_ready) return;
        _debounce.Stop();
        _debounce.Start();
    }

    [RelayCommand]
    private void RunQuery()
    {
        var query = BuildQuery();
        var subnets = HideSameSubnet ? _data.GetLocalSubnets() : null;
        if (UniqueOnly)
        {
            var rows = _data.QueryUnique(query);
            if (subnets is { Count: > 0 })
                rows = rows.Where(r => !InSameSubnet(r.RemoteAddress, subnets)).ToList();
            _rawUnique = rows.ToList();
            foreach (var f in _columnFilters) f.Populate(_rawUnique);
        }
        else
        {
            var rows = _data.Query(query);
            if (subnets is { Count: > 0 })
                rows = rows.Where(r => !InSameSubnet(r.RemoteAddress, subnets)).ToList();
            _rawResults = rows.ToList();
            foreach (var f in _columnFilters) f.Populate(_rawResults);
        }
        ApplyColumnFilters();
    }

    /// <summary>Apply the Excel-style column checklists to the current raw result set.</summary>
    private void ApplyColumnFilters()
    {
        if (UniqueOnly)
        {
            var rows = _rawUnique.Where(r => _columnFilters.All(f => f.Accepts(r))).ToList();
            UniqueResults.Clear();
            foreach (var r in rows) UniqueResults.Add(r);
            ResultSummary = rows.Count == 0 ? "No matching flows."
                : rows.Count == _rawUnique.Count ? $"{rows.Count} distinct flows."
                : $"{rows.Count} of {_rawUnique.Count} distinct flows (column filters).";
        }
        else
        {
            var rows = _rawResults.Where(r => _columnFilters.All(f => f.Accepts(r))).ToList();
            Results.Clear();
            foreach (var r in rows) Results.Add(r);
            ResultSummary = rows.Count == 0 ? "No matching samples."
                : rows.Count == _rawResults.Count ? $"{rows.Count} rows (newest first)."
                : $"{rows.Count} of {_rawResults.Count} rows (column filters).";
        }
    }

    [RelayCommand]
    private Task ExportCsv() => Export(isCsv: true);

    [RelayCommand]
    private Task ExportJson() => Export(isCsv: false);

    private async Task Export(bool isCsv)
    {
        var query = BuildQuery();
        query.Limit = 1_000_000;
        var ext = isCsv ? "csv" : "json";
        var kind = UniqueOnly ? "flows" : "connections";
        var suggested = $"{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}";

        var path = await ResolvePath(suggested, ext);
        if (path is null) return;

        if (UniqueOnly)
        {
            var flows = _data.QueryUnique(query);
            if (isCsv) SampleExporter.WriteCsv(path, flows); else SampleExporter.WriteJson(path, flows);
            ResultSummary = $"Exported {flows.Count} flows to {path}";
        }
        else
        {
            var rows = _data.Query(query);
            if (isCsv) SampleExporter.WriteCsv(path, rows); else SampleExporter.WriteJson(path, rows);
            ResultSummary = $"Exported {rows.Count} rows to {path}";
        }
    }

    [RelayCommand]
    private async Task ExportFirewallPdf()
    {
        var suggested = $"firewall-report-{DateTime.Now:yyyyMMdd-HHmmss}.pdf";
        var path = await ResolvePath(suggested, "pdf");
        if (path is null) return;

        var query = BuildQuery();
        var options = new FirewallReportOptions
        {
            ResolveHostnames = ResolveHostnames,
            SummarizeSourcesToCidr = SummarizeSources,
            LogoPath = FindLogo(),
            FilterSummary = DescribeFilters(),
            Annotations = AnnotationsProvider?.Invoke()
        };
        var report = _data.BuildFirewallReport(query, options);
        FirewallReportPdf.Save(report, path);
        ResultSummary =
            $"Firewall PDF: {report.Inbound.Count} inbound, {report.Outbound.Count} outbound rules → {path}";
    }

    /// <summary>Prompt for a save path; fall back to the export dir if no dialog is wired.</summary>
    private async Task<string?> ResolvePath(string suggestedName, string extension)
    {
        var dir = _exportDirectory();
        Directory.CreateDirectory(dir);
        if (SaveService is not null)
            return await SaveService.SaveAsync(suggestedName, extension, dir);
        return Path.Combine(dir, suggestedName);
    }

    private static string? FindLogo()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "assets", "DependenSee.png");
        return File.Exists(p) ? p : null;
    }

    private string DescribeFilters()
    {
        var parts = new List<string> { $"Last {Math.Max(1, HoursBack)}h", SelectedFamily };
        if (SelectedScope != "Any") parts.Add("scope " + SelectedScope);
        if (SelectedProtocol != "Any") parts.Add(SelectedProtocol);
        if (!string.IsNullOrWhiteSpace(ProcessNameFilter)) parts.Add("process ~ " + ProcessNameFilter);
        return string.Join(" · ", parts);
    }

    private static bool InSameSubnet(string remote, IReadOnlyCollection<string> localSubnets)
    {
        if (string.IsNullOrEmpty(remote)) return false;
        var p = remote.Split('.');
        if (p.Length != 4) return false;
        return localSubnets.Contains($"{p[0]}.{p[1]}.{p[2]}");
    }

    private ConnectionQuery BuildQuery()
    {
        var q = new ConnectionQuery
        {
            From = DateTime.UtcNow.AddHours(-Math.Max(1, HoursBack)),
            ProcessName = string.IsNullOrWhiteSpace(ProcessNameFilter) ? null : ProcessNameFilter,
            RemoteAddress = string.IsNullOrWhiteSpace(RemoteAddressFilter) ? null : RemoteAddressFilter,
            ProcessNotContains = string.IsNullOrWhiteSpace(ProcessNotContains) ? null : ProcessNotContains,
            RemoteNotContains = string.IsNullOrWhiteSpace(RemoteNotContains) ? null : RemoteNotContains,
            LocalAddress = string.IsNullOrWhiteSpace(LocalAddressFilter) ? null : LocalAddressFilter,
            LocalNotContains = string.IsNullOrWhiteSpace(LocalNotContains) ? null : LocalNotContains,
            ExcludeEphemeral = ExcludeEphemeral,
            Limit = Limit <= 0 ? 5000 : Limit
        };
        if (int.TryParse(LocalPortFilter, out var port)) q.LocalPort = port;
        if (int.TryParse(LocalPortNotFilter, out var portNot)) q.LocalPortNot = portNot;

        q.Protocol = SelectedProtocol switch { "Tcp" => Protocol.Tcp, "Udp" => Protocol.Udp, _ => null };
        q.Direction = SelectedDirection switch
        {
            "Listen" => ConnectionDirection.Listen,
            "Inbound" => ConnectionDirection.Inbound,
            "Outbound" => ConnectionDirection.Outbound,
            _ => null
        };
        q.AddressFamily = SelectedFamily switch
        {
            "IPv4" => AddressFamilyOption.IPv4,
            "IPv6" => AddressFamilyOption.IPv6,
            _ => AddressFamilyOption.Any
        };
        q.Scope = SelectedScope switch
        {
            "Private" => IpScope.Private,
            "Internet" => IpScope.Public,
            "Loopback" => IpScope.Loopback,
            "Link-local" => IpScope.LinkLocal,
            _ => null
        };
        return q;
    }
}
