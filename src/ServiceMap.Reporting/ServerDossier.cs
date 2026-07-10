using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using ServiceMap.Core.Analysis;
using ServiceMap.Core.Models;

namespace ServiceMap.Reporting;

/// <summary>One reconciled flow (or unused rule) carried into the dossier.</summary>
public sealed class DossierReconRow
{
    public string Coverage { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    /// <summary>Export-native rule identifier (device group + position / "No.").</summary>
    public string RuleRef { get; set; } = string.Empty;
    public string Policy { get; set; } = string.Empty;
    public string Zones { get; set; } = string.Empty;
    public long Count { get; set; }
}

/// <summary>
/// Everything known about one server, gathered by the app layer and rendered
/// by <see cref="ServerDossierWriter"/> into a workbook + CSVs.
/// </summary>
public sealed class DossierData
{
    public string MachineName { get; set; } = string.Empty;
    public List<string> MachineAddresses { get; } = new();
    public string Wave { get; set; } = string.Empty;
    public DateTime? WindowStart { get; set; }
    public DateTime? WindowEnd { get; set; }
    public int HoursBack { get; set; }
    public long SweepCount { get; set; }
    public string CollectionSource { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
    public string ToolVersion { get; set; } = string.Empty;

    public List<ServiceRecord> Services { get; } = new();
    public List<ConnectionAggregate> Listeners { get; } = new();
    public List<ConnectionAggregate> Inbound { get; } = new();
    public List<ConnectionAggregate> Outbound { get; } = new();
    public List<CrossDependency> CrossDependencies { get; } = new();
    public List<Annotation> Annotations { get; } = new();

    public bool PolicyLoaded { get; set; }
    public List<DossierReconRow> Reconciliation { get; } = new();
    public List<DossierReconRow> UnusedRules { get; } = new();

    public int ReadinessScore { get; set; }
    public string ReadinessRating { get; set; } = string.Empty;
    public List<string> ReadinessNotes { get; } = new();
    public List<RiskFinding> RiskFindings { get; } = new();
    /// <summary>Dependencies first observed in the last 7 days (freeze-drift check).</summary>
    public List<ConnectionAggregate> RecentDependencies { get; } = new();
}

/// <summary>
/// Writes the per-server migration dossier: one Excel workbook (tabbed, for
/// humans) plus one CSV per section (for pipelines), plus a manifest.json.
/// The caller zips the directory and attaches the firewall PDF.
/// </summary>
public static class ServerDossierWriter
{
    /// <summary>Write all dossier files into <paramref name="dir"/>; returns the file names written.</summary>
    public static List<string> Write(DossierData d, string dir)
    {
        Directory.CreateDirectory(dir);
        var files = new List<string>();

        var xlsx = Sanitize(d.MachineName) + "-dossier.xlsx";
        WriteWorkbook(d, Path.Combine(dir, xlsx));
        files.Add(xlsx);

        files.Add(WriteCsv(dir, "services.csv",
            new[] { "name", "display_name", "state", "start_mode", "pid", "account", "executable_path" },
            d.Services.Select(s => new[]
            {
                s.Name, s.DisplayName, s.State, s.StartMode, s.ProcessId.ToString(),
                s.Account ?? "", s.ExecutablePath ?? ""
            })));

        files.Add(WriteCsv(dir, "listening-ports.csv", FlowHeader,
            d.Listeners.Select(FlowRow)));
        files.Add(WriteCsv(dir, "inbound-flows.csv", FlowHeader,
            d.Inbound.Select(FlowRow)));
        files.Add(WriteCsv(dir, "outbound-flows.csv", FlowHeader,
            d.Outbound.Select(FlowRow)));

        files.Add(WriteCsv(dir, "cross-dependencies.csv",
            new[] { "from_machine", "from_wave", "to_machine", "to_wave", "crosses_wave", "process", "protocol", "remote_address", "remote_port", "count" },
            d.CrossDependencies.Select(c => new[]
            {
                c.FromMachine, c.FromWave, c.ToMachine, c.ToWave,
                c.CrossesWaveBoundary ? "yes" : "no",
                c.Process, c.Protocol.ToString(), c.RemoteAddress, c.RemotePort.ToString(), c.SampleCount.ToString()
            })));

        files.Add(WriteCsv(dir, "risk-flags.csv",
            new[] { "severity", "title", "detail" },
            d.RiskFindings.Select(r => new[] { r.Severity, r.Title, r.Detail })));

        files.Add(WriteCsv(dir, "new-dependencies.csv", FlowHeader,
            d.RecentDependencies.Select(FlowRow)));

        files.Add(WriteCsv(dir, "annotations.csv",
            new[] { "kind", "key", "friendly_name", "owner", "criticality", "notes" },
            d.Annotations.Select(a => new[]
            {
                a.Kind.ToString(), a.Key, a.FriendlyName ?? "", a.Owner ?? "",
                a.Criticality.ToString(), a.Notes ?? ""
            })));

        if (d.PolicyLoaded)
        {
            files.Add(WriteCsv(dir, "firewall-reconciliation.csv", ReconHeader,
                d.Reconciliation.Select(ReconRow)));
            files.Add(WriteCsv(dir, "unused-allow-rules.csv", ReconHeader,
                d.UnusedRules.Select(ReconRow)));
        }

        // Machine-readable twin of the whole dossier, for pipelines/CMDB import.
        File.WriteAllText(Path.Combine(dir, "dossier.json"),
            JsonSerializer.Serialize(d, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        files.Add("dossier.json");

        // Target-cloud rule starting points derived from observed flows.
        var cloudDir = Path.Combine(dir, "cloud-rules");
        Directory.CreateDirectory(cloudDir);
        foreach (var (fileName, content) in CloudRuleGenerator.Generate(d))
        {
            File.WriteAllText(Path.Combine(cloudDir, fileName), content);
            files.Add("cloud-rules/" + fileName);
        }

        var manifest = new
        {
            machine = d.MachineName,
            addresses = d.MachineAddresses,
            wave = d.Wave,
            window_start_utc = d.WindowStart,
            window_end_utc = d.WindowEnd,
            hours_back = d.HoursBack,
            sweep_count = d.SweepCount,
            collection_source = d.CollectionSource,
            generated_utc = d.GeneratedUtc,
            tool_version = d.ToolVersion,
            policy_loaded = d.PolicyLoaded,
            counts = new
            {
                services = d.Services.Count,
                listeners = d.Listeners.Count,
                inbound = d.Inbound.Count,
                outbound = d.Outbound.Count,
                cross_dependencies = d.CrossDependencies.Count,
                reconciled_flows = d.Reconciliation.Count,
                unused_allow_rules = d.UnusedRules.Count
            },
            files
        };
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        files.Add("manifest.json");
        return files;
    }

    private static readonly string[] FlowHeader =
        { "direction", "protocol", "scope", "local_address", "local_port", "remote_address", "remote_port", "service_or_process", "process", "first_seen_utc", "last_seen_utc", "count" };

    private static string[] FlowRow(ConnectionAggregate f) => new[]
    {
        f.Direction.ToString(), f.Protocol.ToString(), f.RemoteScope.ToString(),
        f.LocalAddress, f.LocalPort.ToString(), f.RemoteAddress, f.RemotePort.ToString(),
        f.ServiceOrProcess, f.ProcessName,
        f.FirstSeen == default ? "" : f.FirstSeen.ToString("o"),
        f.LastSeen == default ? "" : f.LastSeen.ToString("o"),
        f.SampleCount.ToString()
    };

    private static readonly string[] ReconHeader =
        { "coverage", "direction", "remote_address", "port", "protocol", "service", "rule", "rule_ref", "policy", "zones", "count" };

    private static string[] ReconRow(DossierReconRow r) => new[]
    {
        r.Coverage, r.Direction, r.RemoteAddress, r.Port.ToString(), r.Protocol,
        r.Service, r.Rule, r.RuleRef, r.Policy, r.Zones, r.Count.ToString()
    };

    private static void WriteWorkbook(DossierData d, string path)
    {
        using var wb = new XLWorkbook();

        // ---- Overview ----
        var ov = wb.Worksheets.Add("Overview");
        var rows = new List<(string K, string V)>
        {
            ("Machine", d.MachineName),
            ("Addresses", string.Join(", ", d.MachineAddresses)),
            ("Migration wave", d.Wave.Length > 0 ? d.Wave : "(unassigned)"),
            ("Observation window (UTC)", d.WindowStart is { } ws && d.WindowEnd is { } we
                ? $"{ws:yyyy-MM-dd HH:mm} - {we:yyyy-MM-dd HH:mm}" : "(no data)"),
            ("Query window", $"last {d.HoursBack}h"),
            ("Collection", d.SweepCount > 0
                ? $"{d.SweepCount:N0} sweeps via {SourceLabel(d.CollectionSource)}"
                : SourceLabel(d.CollectionSource)),
            ("Services", d.Services.Count.ToString()),
            ("Listening endpoints", d.Listeners.Count.ToString()),
            ("Inbound dependencies", d.Inbound.Count.ToString()),
            ("Outbound dependencies", d.Outbound.Count.ToString()),
            ("Machine-to-machine dependencies", d.CrossDependencies.Count.ToString()),
            ("Readiness", d.ReadinessRating.Length > 0
                ? $"{d.ReadinessRating} ({d.ReadinessScore}/100)" +
                  (d.ReadinessNotes.Count > 0 ? " - " + string.Join(" ", d.ReadinessNotes) : "")
                : "(not computed)"),
            ("Risk flags", d.RiskFindings.Count == 0
                ? "none"
                : $"{d.RiskFindings.Count(f => f.Severity == "High")} high, " +
                  $"{d.RiskFindings.Count(f => f.Severity == "Medium")} medium, " +
                  $"{d.RiskFindings.Count(f => f.Severity == "Low")} low"),
            ("New dependencies (7d)", d.RecentDependencies.Count.ToString()),
            ("Firewall policy", d.PolicyLoaded
                ? $"reconciled - {d.Reconciliation.Count} flows, {d.UnusedRules.Count} unused allow rules"
                : "(no policy folder configured)"),
            ("Generated (UTC)", d.GeneratedUtc.ToString("yyyy-MM-dd HH:mm")),
            ("Tool version", d.ToolVersion)
        };
        ov.Cell(1, 1).Value = "Carrier DependenSee - Server Migration Dossier";
        ov.Cell(1, 1).Style.Font.SetBold().Font.FontSize = 14;
        for (var i = 0; i < rows.Count; i++)
        {
            ov.Cell(i + 3, 1).SetValue(rows[i].K).Style.Font.SetBold();
            ov.Cell(i + 3, 2).SetValue(rows[i].V);
        }
        ov.Column(1).Width = 34;
        ov.Column(2).Width = 80;

        // ---- Data sheets ----
        Sheet(wb, "Services",
            new[] { "Name", "Display name", "State", "Start mode", "PID", "Account", "Executable" },
            d.Services.Select(s => new object[]
                { s.Name, s.DisplayName, s.State, s.StartMode, s.ProcessId, s.Account ?? "", s.ExecutablePath ?? "" }));

        Sheet(wb, "Listening ports", FlowSheetHeader,
            d.Listeners.Select(FlowSheetRow));
        Sheet(wb, "Inbound flows", FlowSheetHeader,
            d.Inbound.Select(FlowSheetRow));
        Sheet(wb, "Outbound flows", FlowSheetHeader,
            d.Outbound.Select(FlowSheetRow));

        Sheet(wb, "Cross-dependencies",
            new[] { "From", "From wave", "To", "To wave", "Crosses wave", "Process", "Proto", "Remote", "Port", "Count" },
            d.CrossDependencies.Select(c => new object[]
            {
                c.FromMachine, c.FromWave, c.ToMachine, c.ToWave,
                c.CrossesWaveBoundary ? "YES" : "", c.Process, c.Protocol.ToString(),
                c.RemoteAddress, c.RemotePort, c.SampleCount
            }));

        if (d.PolicyLoaded)
        {
            Sheet(wb, "Firewall reconciliation", ReconSheetHeader,
                d.Reconciliation.Select(ReconSheetRow));
            Sheet(wb, "Unused allow rules", ReconSheetHeader,
                d.UnusedRules.Select(ReconSheetRow));
        }

        Sheet(wb, "Risk flags",
            new[] { "Severity", "Finding", "Detail" },
            d.RiskFindings.Select(r => new object[] { r.Severity, r.Title, r.Detail }));

        Sheet(wb, "New dependencies (7d)", FlowSheetHeader,
            d.RecentDependencies.Select(FlowSheetRow));

        Sheet(wb, "Annotations",
            new[] { "Kind", "Key", "Friendly name", "Owner", "Criticality", "Notes" },
            d.Annotations.Select(a => new object[]
                { a.Kind.ToString(), a.Key, a.FriendlyName ?? "", a.Owner ?? "", a.Criticality.ToString(), a.Notes ?? "" }));

        wb.SaveAs(path);
    }

    private static readonly string[] FlowSheetHeader =
        { "Direction", "Proto", "Scope", "Local address", "L.Port", "Remote address", "R.Port", "Service/Process", "First seen (UTC)", "Last seen (UTC)", "Count" };

    private static object[] FlowSheetRow(ConnectionAggregate f) => new object[]
    {
        f.Direction.ToString(), f.Protocol.ToString(), f.RemoteScope.ToString(),
        f.LocalAddress, f.LocalPort, f.RemoteAddress, f.RemotePort, f.ServiceOrProcess,
        f.FirstSeen == default ? "" : f.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"),
        f.LastSeen == default ? "" : f.LastSeen.ToString("yyyy-MM-dd HH:mm:ss"),
        f.SampleCount
    };

    private static readonly string[] ReconSheetHeader =
        { "Coverage", "Direction", "Remote address", "Port", "Proto", "Service", "Rule", "Rule #", "Policy", "Zones", "Count" };

    private static object[] ReconSheetRow(DossierReconRow r) => new object[]
        { r.Coverage, r.Direction, r.RemoteAddress, r.Port, r.Protocol, r.Service, r.Rule, r.RuleRef, r.Policy, r.Zones, r.Count };

    /// <summary>Add a data sheet: bold frozen header, autofilter, sensible widths.</summary>
    private static void Sheet(XLWorkbook wb, string name, string[] header, IEnumerable<object[]> rows)
    {
        var ws = wb.Worksheets.Add(name);
        for (var c = 0; c < header.Length; c++)
            ws.Cell(1, c + 1).SetValue(header[c]).Style.Font.SetBold();

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case int i: cell.SetValue(i); break;
                    case long l: cell.SetValue(l); break;
                    default: cell.SetValue(row[c]?.ToString() ?? ""); break;
                }
            }
            r++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, Math.Max(1, r - 1), header.Length).SetAutoFilter();
        // AdjustToContents is slow on large sheets; use generous fixed widths.
        for (var c = 1; c <= header.Length; c++) ws.Column(c).Width = 18;
    }

    private static string SourceLabel(string source) => source switch
    {
        "local-collector" => "local collector (continuous)",
        "remote-scan" => "remote scan (agentless snapshots)",
        _ => "unknown source"
    };

    private static string WriteCsv(string dir, string name, string[] header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", header));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Q)));
        File.WriteAllText(Path.Combine(dir, name), sb.ToString());
        return name;
    }

    private static string Q(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 0 ? name : "server";
    }
}
