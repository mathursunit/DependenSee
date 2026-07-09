using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.App.Services;
using ServiceMap.Core.Models;
using ServiceMap.Core.Net;
using ServiceMap.Core.Storage;
using ServiceMap.Firewall.Matching;

namespace ServiceMap.App.ViewModels;

/// <summary>One reconciled flow: observed connection matched against firewall policy.</summary>
public sealed class FwReconRow
{
    public string Direction { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public string RemoteObject { get; set; } = string.Empty;
    public string Groups { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public string Policy { get; set; } = string.Empty;
    public string Zones { get; set; } = string.Empty;
    public long Count { get; set; }
}

/// <summary>
/// Firewall integration: load exported policy, then reconcile a server's observed
/// connections against it (Covered / Gap / Denied) and enrich each remote IP with
/// its firewall object and group membership. Reverse reconciliation lists allow
/// rules covering the machine that no observed flow ever exercised.
/// </summary>
public sealed partial class FirewallViewModel : ViewModelBase
{
    private readonly MultiSourceDataAccess _multi;
    private readonly Func<string> _localDbPath;
    private readonly Action<string> _onFolderChanged;
    private readonly FirewallService _fw = new();
    private readonly List<FwReconRow> _allRows = new();

    // State from the last Reconcile, needed by reverse reconciliation.
    private readonly HashSet<string> _exercisedRules = new(StringComparer.OrdinalIgnoreCase);
    private string _machineIp = string.Empty;
    private bool _reconciled;

    public IFileSaveService? SaveService { get; set; }

    public FirewallViewModel(MultiSourceDataAccess multi, Func<string> localDbPath,
                             string initialFolder, Action<string> onFolderChanged)
    {
        _multi = multi;
        _localDbPath = localDbPath;
        _onFolderChanged = onFolderChanged;
        _policyFolder = initialFolder;
        RefreshMachines();
        if (!string.IsNullOrWhiteSpace(initialFolder)) LoadPolicy();
    }

    public ObservableCollection<MachineOption> MachineOptions { get; } = new();
    public ObservableCollection<FwReconRow> Results { get; } = new();
    public string[] CoverageOptions { get; } = { "All", "Gap", "Covered", "Denied", "Unused" };

    [ObservableProperty] private string _policyFolder = string.Empty;
    [ObservableProperty] private MachineOption? _selectedMachine;
    [ObservableProperty] private int _hoursBack = 168;
    [ObservableProperty] private string _selectedCoverage = "All";
    [ObservableProperty] private string _policyStatus = "Point at the folder of firewall CSV exports and click Load policy.";
    [ObservableProperty] private string _reconStatus = "Load a policy and pick a server, then Reconcile.";

    partial void OnPolicyFolderChanged(string value) => _onFolderChanged(value ?? string.Empty);
    partial void OnSelectedCoverageChanged(string value) => ApplyFilter();

    public void RefreshMachines()
    {
        var prev = SelectedMachine?.Name;
        MachineOptions.Clear();
        MachineOptions.Add(new MachineOption("Local (this machine)", null));
        foreach (var m in _multi.GetMachines())
            MachineOptions.Add(new MachineOption(m.Name, m.DatabasePath));
        SelectedMachine = MachineOptions.FirstOrDefault(o => o.Name == prev) ?? MachineOptions[0];
    }

    [RelayCommand]
    private void LoadPolicy()
    {
        if (string.IsNullOrWhiteSpace(PolicyFolder)) { PolicyStatus = "Enter the policy folder path."; return; }
        if (_fw.Load(PolicyFolder) && _fw.Policy is { } p)
        {
            var unresolved = p.UnresolvedReferences.Count;
            PolicyStatus = $"Loaded {_fw.LoadedFiles.Count} file(s): {string.Join(", ", _fw.LoadedFiles)} — " +
                           $"{p.Groups.Count} address groups · {p.Egress.Count} egress · " +
                           $"{p.Ingress.Count} ingress · {p.OnPrem.Count} on-prem rules." +
                           (unresolved > 0
                               ? $" ⚠ {unresolved} rule reference(s) resolve to no network (FQDN/object names without " +
                                 "an embedded IP) — flows those rules cover will show as Gap."
                               : string.Empty);
        }
        else
        {
            PolicyStatus = "Could not load policy: " + (_fw.Error ?? "no CSV files found.");
        }
    }

    [RelayCommand]
    private void Reconcile()
    {
        if (_fw.Policy is not { } policy) { ReconStatus = "Load a firewall policy first."; return; }
        if (SelectedMachine is null) { ReconStatus = "Pick a server."; return; }

        var dbPath = SelectedMachine.DbPath ?? _localDbPath();
        var data = new DataAccess(() => dbPath);
        var flows = data.QueryUnique(new ConnectionQuery
        {
            From = DateTime.UtcNow.AddHours(-Math.Max(1, HoursBack)),
            AddressFamily = AddressFamilyOption.IPv4,
            Limit = 1_000_000
        });
        var machineIp = PickPrimaryIp(data.GetLocalAddresses(), flows);

        _allRows.Clear();
        _exercisedRules.Clear();
        _machineIp = machineIp;
        int covered = 0, gap = 0, denied = 0, unknown = 0;
        foreach (var f in flows)
        {
            if (f.Direction is not (ConnectionDirection.Inbound or ConnectionDirection.Outbound)) continue;
            var outbound = f.Direction == ConnectionDirection.Outbound;
            var port = outbound ? f.RemotePort : f.LocalPort;
            // Unique aggregation blanks the machine's own address for non-listeners; supply it.
            var localAddr = !string.IsNullOrEmpty(f.OwnerAddress) ? f.OwnerAddress
                          : !string.IsNullOrEmpty(f.LocalAddress) ? f.LocalAddress : machineIp;
            var key = new FlowKey(localAddr, f.RemoteAddress, f.LocalPort, f.RemotePort, outbound,
                                  f.Protocol.ToString().ToLowerInvariant());
            var m = policy.MatchFlow(key);
            var e = policy.Enrich(f.RemoteAddress);

            switch (m.Coverage)
            {
                case FwCoverage.Covered: covered++; break;
                case FwCoverage.Denied: denied++; break;
                case FwCoverage.Gap: gap++; break;
                default: unknown++; break;
            }
            if (m.RuleName is { Length: > 0 } rn) _exercisedRules.Add(rn);

            _allRows.Add(new FwReconRow
            {
                Direction = f.Direction.ToString(),
                RemoteAddress = f.RemoteAddress,
                RemoteObject = e.ObjectName ?? (e.Groups.FirstOrDefault() ?? ""),
                Groups = string.Join(", ", e.Groups.Take(3)),
                Port = port,
                Protocol = f.Protocol.ToString(),
                Service = f.ServiceOrProcess,
                Coverage = m.Coverage.ToString(),
                Rule = m.RuleName ?? "",
                Policy = m.Policy ?? "",
                Zones = ZoneText(m.SourceZone, m.DestZone),
                Count = f.SampleCount
            });
        }

        _reconciled = true;
        ApplyFilter();
        ReconStatus = $"{SelectedMachine.Name}: {_allRows.Count} flows — " +
                      $"{covered} covered, {gap} gaps, {denied} denied" +
                      (unknown > 0 ? $", {unknown} unresolved" : "") + ".";
    }

    /// <summary>
    /// Reverse reconciliation: allow rules that cover this machine's IP but were
    /// never exercised by any observed flow in the reconciled window — prime
    /// candidates for tightening or decommissioning at migration time.
    /// </summary>
    [RelayCommand]
    private void FindUnusedRules()
    {
        if (_fw.Policy is not { } policy) { ReconStatus = "Load a firewall policy first."; return; }
        if (!_reconciled) { ReconStatus = "Run Reconcile first — unused rules are relative to the observed window."; return; }
        if (string.IsNullOrEmpty(_machineIp)) { ReconStatus = "No usable machine IP found to match rules against."; return; }

        _allRows.RemoveAll(r => r.Coverage == "Unused");
        var candidates = policy.AllowRulesCovering(_machineIp)
            .Where(r => !_exercisedRules.Contains(r.Name))
            .ToList();

        foreach (var r in candidates)
        {
            _allRows.Add(new FwReconRow
            {
                Direction = "—",
                RemoteAddress = "",
                RemoteObject = Truncate(string.Join(", ", r.Destinations), 60),
                Groups = Truncate(string.Join(", ", r.Sources), 60),
                Port = 0,
                Protocol = "",
                Service = Truncate(string.Join(", ", r.Services.Concat(r.Applications).Distinct()), 40),
                Coverage = "Unused",
                Rule = r.Name,
                Policy = r.Policy + (string.IsNullOrEmpty(r.Usage) ? "" : $" · usage: {r.Usage}"),
                Zones = ZoneText(r.SourceZone, r.DestZone),
                Count = 0
            });
        }

        SelectedCoverage = "Unused";
        ApplyFilter();
        ReconStatus = $"{candidates.Count} allow rule(s) cover {_machineIp} but were not exercised by any " +
                      $"observed flow in the last {Math.Max(1, HoursBack)}h. (Rules whose sources/destinations " +
                      "cannot be resolved to networks are not listed.)";
    }

    private void ApplyFilter()
    {
        Results.Clear();
        IEnumerable<FwReconRow> rows = _allRows;
        if (SelectedCoverage != "All")
            rows = rows.Where(r => r.Coverage.Equals(SelectedCoverage, StringComparison.OrdinalIgnoreCase));
        foreach (var r in rows.OrderBy(r => r.Coverage).ThenByDescending(r => r.Count)) Results.Add(r);
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        // Honor the coverage filter: export what's on screen.
        var rows = SelectedCoverage == "All"
            ? _allRows
            : _allRows.Where(r => r.Coverage.Equals(SelectedCoverage, StringComparison.OrdinalIgnoreCase)).ToList();
        if (rows.Count == 0) { ReconStatus = "Nothing to export — run Reconcile first."; return; }
        if (SaveService is null) return;
        var suffix = SelectedCoverage == "All" ? "" : "-" + SelectedCoverage.ToLowerInvariant();
        var name = $"{(SelectedMachine?.Name ?? "server")}-fw-reconciliation{suffix}-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        var path = await SaveService.SaveAsync(name, "csv", null);
        if (path is null) return;

        var sb = new StringBuilder();
        sb.AppendLine("coverage,direction,remote_address,remote_object,groups,port,protocol,service,rule,policy,zones,count");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", new[]
            {
                r.Coverage, r.Direction, r.RemoteAddress, Q(r.RemoteObject), Q(r.Groups), r.Port.ToString(),
                r.Protocol, Q(r.Service), Q(r.Rule), Q(r.Policy), Q(r.Zones), r.Count.ToString()
            }));
        File.WriteAllText(path, sb.ToString());
        ReconStatus = $"Exported {rows.Count} rows to {path}";
    }

    /// <summary>
    /// The machine's primary IP for rule matching. Prefer the address that
    /// actually carries observed traffic (most frequent owner address), then
    /// fall back to a strictly-private address (RFC1918/CGNAT via IpClassifier —
    /// a bare "172." prefix check would happily pick a WSL/Hyper-V NAT adapter).
    /// </summary>
    internal static string PickPrimaryIp(IEnumerable<string> ips, IReadOnlyList<ConnectionAggregate>? flows = null)
    {
        if (flows is not null)
        {
            var byTraffic = flows
                .Where(f => !string.IsNullOrEmpty(f.OwnerAddress) && IpClassifier.IsIPv4(f.OwnerAddress)
                            && IpClassifier.Classify(f.OwnerAddress) == IpScope.Private)
                .GroupBy(f => f.OwnerAddress)
                .OrderByDescending(g => g.Sum(f => f.SampleCount))
                .Select(g => g.Key)
                .FirstOrDefault();
            if (byTraffic is not null) return byTraffic;
        }

        var v4 = ips.Where(IpClassifier.IsIPv4)
                    .Where(ip => ip is not ("0.0.0.0" or "127.0.0.1"))
                    .ToList();
        return v4.FirstOrDefault(ip => IpClassifier.Classify(ip) == IpScope.Private)
               ?? v4.FirstOrDefault() ?? string.Empty;
    }

    private static string ZoneText(string? src, string? dst) =>
        string.IsNullOrEmpty(src) && string.IsNullOrEmpty(dst) ? "" : $"{src}→{dst}";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Q(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
