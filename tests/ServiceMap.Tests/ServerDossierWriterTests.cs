using ClosedXML.Excel;
using ServiceMap.Core.Models;
using ServiceMap.Reporting;
using Xunit;

namespace ServiceMap.Tests;

public sealed class ServerDossierWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"cds-dossier-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static DossierData Sample()
    {
        var d = new DossierData
        {
            MachineName = "APPSRV01",
            Wave = "Wave 2",
            HoursBack = 168,
            SweepCount = 1200,
            CollectionSource = "local-collector",
            ToolVersion = "1.10.0"
        };
        d.MachineAddresses.Add("10.10.5.20");
        d.Services.Add(new ServiceRecord
        {
            Name = "W3SVC", DisplayName = "World Wide Web Publishing Service",
            State = "Running", StartMode = "Auto", ProcessId = 4321, Account = "LocalSystem"
        });
        d.Listeners.Add(new ConnectionAggregate
        {
            Protocol = Protocol.Tcp, Direction = ConnectionDirection.Listen,
            LocalAddress = "0.0.0.0", LocalPort = 443, ProcessName = "w3wp", SampleCount = 500
        });
        d.Inbound.Add(new ConnectionAggregate
        {
            Protocol = Protocol.Tcp, Direction = ConnectionDirection.Inbound,
            LocalPort = 443, RemoteAddress = "10.2.0.99", ProcessName = "w3wp",
            ServiceName = "World Wide Web Publishing Service", SampleCount = 220,
            FirstSeen = DateTime.UtcNow.AddDays(-3), LastSeen = DateTime.UtcNow
        });
        d.Outbound.Add(new ConnectionAggregate
        {
            Protocol = Protocol.Tcp, Direction = ConnectionDirection.Outbound,
            RemoteAddress = "10.60.1.5", RemotePort = 1433, ProcessName = "w3wp", SampleCount = 90
        });
        d.CrossDependencies.Add(new CrossDependency
        {
            FromMachine = "APPSRV01", ToMachine = "SQL01", FromWave = "Wave 2", ToWave = "Wave 1",
            Process = "w3wp", Protocol = Protocol.Tcp, RemoteAddress = "10.60.1.5", RemotePort = 1433, SampleCount = 90
        });
        d.Annotations.Add(new Annotation
        {
            Kind = AnnotationKind.Port, Key = "1433", FriendlyName = "Finance DB",
            Owner = "J. Lee", Criticality = Criticality.High
        });
        d.PolicyLoaded = true;
        d.Reconciliation.Add(new DossierReconRow
        {
            Coverage = "Covered", Direction = "Outbound", RemoteAddress = "10.60.1.5",
            Port = 1433, Protocol = "Tcp", Service = "w3wp", Rule = "OnPrem SQL",
            Policy = "Checkpoint", Count = 90
        });
        d.UnusedRules.Add(new DossierReconRow
        {
            Coverage = "Unused", Rule = "Old FTP Allow", Policy = "CWAN Egress · usage: Unused"
        });
        return d;
    }

    [Fact]
    public void WritesWorkbookCsvsAndManifest()
    {
        var files = ServerDossierWriter.Write(Sample(), _dir);

        Assert.Contains("APPSRV01-dossier.xlsx", files);
        Assert.Contains("manifest.json", files);
        foreach (var f in files)
            Assert.True(File.Exists(Path.Combine(_dir, f)), $"missing {f}");

        // Policy-dependent sections present because PolicyLoaded = true.
        Assert.Contains("firewall-reconciliation.csv", files);
        Assert.Contains("unused-allow-rules.csv", files);
        Assert.Contains("dossier.json", files);
        Assert.Contains("risk-flags.csv", files);
        Assert.Contains("cloud-rules/aws-security-group.tf", files);
        Assert.Contains("cloud-rules/azure-nsg.tf", files);
    }

    [Fact]
    public void WorkbookHasExpectedSheetsAndData()
    {
        ServerDossierWriter.Write(Sample(), _dir);
        using var wb = new XLWorkbook(Path.Combine(_dir, "APPSRV01-dossier.xlsx"));

        var sheets = wb.Worksheets.Select(w => w.Name).ToList();
        Assert.Contains("Overview", sheets);
        Assert.Contains("Services", sheets);
        Assert.Contains("Listening ports", sheets);
        Assert.Contains("Inbound flows", sheets);
        Assert.Contains("Outbound flows", sheets);
        Assert.Contains("Cross-dependencies", sheets);
        Assert.Contains("Firewall reconciliation", sheets);
        Assert.Contains("Unused allow rules", sheets);
        Assert.Contains("Risk flags", sheets);
        Assert.Contains("New dependencies (7d)", sheets);
        Assert.Contains("Annotations", sheets);

        var services = wb.Worksheet("Services");
        Assert.Equal("W3SVC", services.Cell(2, 1).GetString());

        var inbound = wb.Worksheet("Inbound flows");
        Assert.Equal("10.2.0.99", inbound.Cell(2, 6).GetString());
    }

    [Fact]
    public void SkipsPolicySectionsWhenNoPolicy()
    {
        var d = Sample();
        d.PolicyLoaded = false;
        var files = ServerDossierWriter.Write(d, _dir);

        Assert.DoesNotContain("firewall-reconciliation.csv", files);
        using var wb = new XLWorkbook(Path.Combine(_dir, "APPSRV01-dossier.xlsx"));
        Assert.DoesNotContain("Firewall reconciliation", wb.Worksheets.Select(w => w.Name));
    }

    [Fact]
    public void CsvEscapesCommasAndQuotes()
    {
        var d = Sample();
        d.Annotations[0].Notes = "moves with \"Finance\", not before";
        ServerDossierWriter.Write(d, _dir);
        var csv = File.ReadAllText(Path.Combine(_dir, "annotations.csv"));
        Assert.Contains("\"moves with \"\"Finance\"\", not before\"", csv);
    }
}
