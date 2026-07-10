using ClosedXML.Excel;
using ServiceMap.Core.Models;

namespace ServiceMap.Reporting;

/// <summary>One machine's row in the fleet workbook.</summary>
public sealed class FleetMachineSummary
{
    public string Name { get; set; } = string.Empty;
    public string Wave { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Addresses { get; set; } = string.Empty;
    public DateTime? WindowStart { get; set; }
    public DateTime? WindowEnd { get; set; }
    public long SweepCount { get; set; }
    public int FlowCount { get; set; }
    public int InboundCount { get; set; }
    public int OutboundCount { get; set; }
    public int ReadinessScore { get; set; }
    public string ReadinessRating { get; set; } = string.Empty;
    public int NewDeps7d { get; set; }
    public int HighRiskFindings { get; set; }
}

/// <summary>
/// Estate-level workbook: machine inventory with readiness, the full
/// machine-to-machine dependency list, and a wave rollup highlighting
/// dependencies that cross wave boundaries — the input to wave-planning calls.
/// </summary>
public static class FleetWorkbookWriter
{
    public static void Write(IReadOnlyList<FleetMachineSummary> machines,
        IReadOnlyList<CrossDependency> crossDeps, string path)
    {
        using var wb = new XLWorkbook();

        // ---- Machines ----
        var ws = wb.Worksheets.Add("Machines");
        var header = new[]
        {
            "Machine", "Wave", "Readiness", "Score", "New deps (7d)", "High-risk findings",
            "Flows", "Inbound", "Outbound", "Sweeps", "Source", "Window start (UTC)", "Window end (UTC)", "Addresses"
        };
        for (var c = 0; c < header.Length; c++)
            ws.Cell(1, c + 1).SetValue(header[c]).Style.Font.SetBold();
        var row = 2;
        foreach (var m in machines.OrderBy(m => m.Wave).ThenBy(m => m.Name))
        {
            ws.Cell(row, 1).SetValue(m.Name);
            ws.Cell(row, 2).SetValue(m.Wave);
            ws.Cell(row, 3).SetValue(m.ReadinessRating);
            ws.Cell(row, 4).SetValue(m.ReadinessScore);
            ws.Cell(row, 5).SetValue(m.NewDeps7d);
            ws.Cell(row, 6).SetValue(m.HighRiskFindings);
            ws.Cell(row, 7).SetValue(m.FlowCount);
            ws.Cell(row, 8).SetValue(m.InboundCount);
            ws.Cell(row, 9).SetValue(m.OutboundCount);
            ws.Cell(row, 10).SetValue(m.SweepCount);
            ws.Cell(row, 11).SetValue(m.Source);
            ws.Cell(row, 12).SetValue(m.WindowStart?.ToString("yyyy-MM-dd HH:mm") ?? "");
            ws.Cell(row, 13).SetValue(m.WindowEnd?.ToString("yyyy-MM-dd HH:mm") ?? "");
            ws.Cell(row, 14).SetValue(m.Addresses);
            if (m.ReadinessRating == "Insufficient")
                ws.Cell(row, 3).Style.Font.FontColor = XLColor.Red;
            row++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, Math.Max(1, row - 1), header.Length).SetAutoFilter();
        for (var c = 1; c <= header.Length; c++) ws.Column(c).Width = 16;

        // ---- Cross-dependencies ----
        var xd = wb.Worksheets.Add("Cross-dependencies");
        var xh = new[] { "From", "From wave", "To", "To wave", "Crosses wave", "Process", "Proto", "Remote", "Port", "Count" };
        for (var c = 0; c < xh.Length; c++) xd.Cell(1, c + 1).SetValue(xh[c]).Style.Font.SetBold();
        row = 2;
        foreach (var d in crossDeps.OrderByDescending(d => d.CrossesWaveBoundary).ThenBy(d => d.FromMachine))
        {
            xd.Cell(row, 1).SetValue(d.FromMachine);
            xd.Cell(row, 2).SetValue(d.FromWave);
            xd.Cell(row, 3).SetValue(d.ToMachine);
            xd.Cell(row, 4).SetValue(d.ToWave);
            xd.Cell(row, 5).SetValue(d.CrossesWaveBoundary ? "YES" : "");
            xd.Cell(row, 6).SetValue(d.Process);
            xd.Cell(row, 7).SetValue(d.Protocol.ToString());
            xd.Cell(row, 8).SetValue(d.RemoteAddress);
            xd.Cell(row, 9).SetValue(d.RemotePort);
            xd.Cell(row, 10).SetValue(d.SampleCount);
            if (d.CrossesWaveBoundary) xd.Cell(row, 5).Style.Font.FontColor = XLColor.Red;
            row++;
        }
        xd.SheetView.FreezeRows(1);
        xd.Range(1, 1, Math.Max(1, row - 1), xh.Length).SetAutoFilter();
        for (var c = 1; c <= xh.Length; c++) xd.Column(c).Width = 16;

        // ---- Wave rollup ----
        var wr = wb.Worksheets.Add("Wave rollup");
        wr.Cell(1, 1).SetValue("Wave").Style.Font.SetBold();
        wr.Cell(1, 2).SetValue("Machines").Style.Font.SetBold();
        wr.Cell(1, 3).SetValue("Ready").Style.Font.SetBold();
        wr.Cell(1, 4).SetValue("Cross-wave deps out").Style.Font.SetBold();
        row = 2;
        foreach (var g in machines.GroupBy(m => m.Wave.Length > 0 ? m.Wave : "(unassigned)").OrderBy(g => g.Key))
        {
            var wave = g.Key;
            wr.Cell(row, 1).SetValue(wave);
            wr.Cell(row, 2).SetValue(g.Count());
            wr.Cell(row, 3).SetValue(g.Count(m => m.ReadinessRating == "Ready"));
            wr.Cell(row, 4).SetValue(crossDeps.Count(d =>
                d.CrossesWaveBoundary &&
                string.Equals(d.FromWave.Length > 0 ? d.FromWave : "(unassigned)", wave, StringComparison.OrdinalIgnoreCase)));
            row++;
        }
        wr.Cell(row + 1, 1).SetValue(
            $"Cross-wave dependencies total: {crossDeps.Count(d => d.CrossesWaveBoundary)} " +
            "(each must survive its waves migrating at different times).").Style.Font.SetItalic();
        for (var c = 1; c <= 4; c++) wr.Column(c).Width = 22;

        wb.SaveAs(path);
    }
}
