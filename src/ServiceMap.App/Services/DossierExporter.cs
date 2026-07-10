using System.IO.Compression;
using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;
using ServiceMap.Firewall.Matching;
using ServiceMap.Reporting;
using ServiceMap.App.ViewModels;

namespace ServiceMap.App.Services;

/// <summary>
/// Builds the per-server migration dossier: gathers everything known about one
/// machine (services, flows, cross-dependencies, annotations, firewall
/// reconciliation when a policy folder is configured), writes the Excel
/// workbook + CSVs + manifest via <see cref="ServerDossierWriter"/>, adds the
/// firewall PDF, and zips it all into a single artifact.
/// </summary>
public static class DossierExporter
{
    public static string Export(
        string zipPath,
        string machineName,
        string dbPath,
        string wave,
        int hoursBack,
        MultiSourceDataAccess multi,
        IReadOnlyDictionary<string, Annotation> annotations,
        string policyFolder,
        string? logoPath)
    {
        var data = new DataAccess(() => dbPath);
        var window = new ConnectionQuery
        {
            From = DateTime.UtcNow.AddHours(-Math.Max(1, hoursBack)),
            Limit = 1_000_000
        };
        var flows = data.QueryUnique(window);

        var d = new DossierData
        {
            MachineName = machineName,
            Wave = wave,
            HoursBack = Math.Max(1, hoursBack),
            SweepCount = long.TryParse(data.GetMeta("sweep_count"), out var sc) ? sc : 0,
            CollectionSource = data.GetMeta("collection_source") ?? string.Empty,
            ToolVersion = AppInfo.Version
        };
        d.MachineAddresses.AddRange(data.GetLocalAddresses());
        d.Services.AddRange(data.GetLatestServices());
        d.Listeners.AddRange(flows.Where(f => f.Direction == ConnectionDirection.Listen));
        d.Inbound.AddRange(flows.Where(f => f.Direction == ConnectionDirection.Inbound));
        d.Outbound.AddRange(flows.Where(f => f.Direction == ConnectionDirection.Outbound));
        d.CrossDependencies.AddRange(multi.DetectCrossDependencies(window).Where(c =>
            string.Equals(c.FromMachine, machineName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.ToMachine, machineName, StringComparison.OrdinalIgnoreCase)));
        d.Annotations.AddRange(annotations.Values.OrderBy(a => a.Kind).ThenBy(a => a.Key));

        if (flows.Count > 0)
        {
            d.WindowStart = flows.Min(f => f.FirstSeen);
            d.WindowEnd = flows.Max(f => f.LastSeen);
        }

        // Firewall reconciliation, when a policy folder is configured.
        var fw = new FirewallService();
        if (!string.IsNullOrWhiteSpace(policyFolder) && fw.Load(policyFolder) && fw.Policy is { } policy)
        {
            d.PolicyLoaded = true;
            var machineIp = FirewallViewModel.PickPrimaryIp(d.MachineAddresses, flows);
            var exercised = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in flows.Where(f =>
                         f.Direction is ConnectionDirection.Inbound or ConnectionDirection.Outbound))
            {
                var outbound = f.Direction == ConnectionDirection.Outbound;
                var localAddr = !string.IsNullOrEmpty(f.OwnerAddress) ? f.OwnerAddress
                              : !string.IsNullOrEmpty(f.LocalAddress) ? f.LocalAddress : machineIp;
                var m = policy.MatchFlow(new FlowKey(localAddr, f.RemoteAddress, f.LocalPort, f.RemotePort,
                    outbound, f.Protocol.ToString().ToLowerInvariant()));
                if (m.RuleName is { Length: > 0 } rn) exercised.Add(rn);
                d.Reconciliation.Add(new DossierReconRow
                {
                    Coverage = m.Coverage.ToString(),
                    Direction = f.Direction.ToString(),
                    RemoteAddress = f.RemoteAddress,
                    Port = outbound ? f.RemotePort : f.LocalPort,
                    Protocol = f.Protocol.ToString(),
                    Service = f.ServiceOrProcess,
                    Rule = m.RuleName ?? "",
                    Policy = m.Policy ?? "",
                    Zones = ZoneText(m.SourceZone, m.DestZone),
                    Count = f.SampleCount
                });
            }

            foreach (var r in policy.AllowRulesCovering(machineIp)
                         .Where(r => !exercised.Contains(r.Name)))
            {
                d.UnusedRules.Add(new DossierReconRow
                {
                    Coverage = "Unused",
                    Direction = "-",
                    Service = string.Join(", ", r.Services.Concat(r.Applications).Distinct()),
                    Rule = r.Name,
                    Policy = r.Policy + (string.IsNullOrEmpty(r.Usage) ? "" : $" · usage: {r.Usage}"),
                    Zones = ZoneText(r.SourceZone, r.DestZone)
                });
            }
        }

        // Stage everything in a temp dir, add the firewall PDF, then zip.
        var stage = Path.Combine(Path.GetTempPath(), "cds-dossier-" + Guid.NewGuid().ToString("N"));
        try
        {
            ServerDossierWriter.Write(d, stage);

            var pdfQuery = new ConnectionQuery
            {
                From = DateTime.UtcNow.AddHours(-Math.Max(1, hoursBack)),
                AddressFamily = AddressFamilyOption.IPv4,
                Limit = 1_000_000
            };
            var report = data.BuildFirewallReport(pdfQuery, new FirewallReportOptions
            {
                ResolveHostnames = true,
                LogoPath = logoPath,
                FilterSummary = $"{machineName} · last {Math.Max(1, hoursBack)}h · IPv4",
                Annotations = annotations
            });
            FirewallReportPdf.Save(report, Path.Combine(stage, Sanitize(machineName) + "-firewall.pdf"));

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(stage, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return zipPath;
        }
        finally
        {
            try { Directory.Delete(stage, recursive: true); } catch { /* temp cleanup */ }
        }
    }

    private static string ZoneText(string? src, string? dst) =>
        string.IsNullOrEmpty(src) && string.IsNullOrEmpty(dst) ? "" : $"{src}→{dst}";

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 0 ? name : "server";
    }
}
