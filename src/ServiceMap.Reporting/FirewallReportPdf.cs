using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using ServiceMap.Core.Models;
using ServiceMap.Core.Net;

namespace ServiceMap.Reporting;

/// <summary>Renders a <see cref="FirewallReport"/> to a PDF using MigraDoc.</summary>
public static class FirewallReportPdf
{
    private static readonly Color Ink = new(31, 41, 55);
    private static readonly Color Accent = new(37, 99, 235);
    private static readonly Color Green = new(5, 150, 105);
    private static readonly Color Panel = new(243, 244, 246);
    private static readonly Color HeaderBg = new(31, 41, 55);
    private static readonly Color Muted = new(107, 114, 128);

    public static void Save(FirewallReport report, string path)
    {
        var doc = BuildDocument(report);
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        renderer.PdfDocument.Save(path);
    }

    private static Document BuildDocument(FirewallReport report)
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new ReportFontResolver();

        var doc = new Document();
        doc.Info.Title = "Carrier DependenSee - Firewall Rule Report";
        doc.Info.Author = "Carrier DependenSee";

        var normal = doc.Styles["Normal"]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.Letter;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);

        AddHeader(section, report);
        AddSummary(section, report);
        AddInboundTable(section, report);
        AddOutboundTable(section, report);
        AddCaveats(section);
        AddFooter(section);
        return doc;
    }

    private static void AddHeader(Section section, FirewallReport report)
    {
        if (!string.IsNullOrEmpty(report.LogoPath) && File.Exists(report.LogoPath))
        {
            var img = section.AddImage(report.LogoPath);
            img.Height = Unit.FromCentimeter(2.2);
            img.LockAspectRatio = true;
        }

        var title = section.AddParagraph("Carrier DependenSee");
        title.Format.Font.Size = 20;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Ink;
        title.Format.SpaceBefore = Unit.FromPoint(2);

        var sub = section.AddParagraph("Firewall Rule Report");
        sub.Format.Font.Size = 12;
        sub.Format.Font.Color = Accent;
        sub.Format.SpaceAfter = Unit.FromPoint(8);

        var meta = section.AddParagraph();
        meta.Format.Font.Size = 9;
        meta.Format.Font.Color = Muted;
        AddLabelled(meta, "Machine", report.MachineName);
        meta.AddLineBreak();
        AddLabelled(meta, "Addresses",
            report.MachineAddresses.Count > 0 ? string.Join(", ", report.MachineAddresses) : "(none observed)");
        meta.AddLineBreak();
        AddLabelled(meta, "Observation window", FormatWindow(report));
        meta.AddLineBreak();
        AddLabelled(meta, "Collection coverage", FormatCoverage(report));
        meta.AddLineBreak();
        AddLabelled(meta, "Filters", report.FilterSummary);
        meta.AddLineBreak();
        AddLabelled(meta, "Generated", report.GeneratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        meta.Format.SpaceAfter = Unit.FromPoint(10);
    }

    private static void AddSummary(Section section, FirewallReport report)
    {
        var p = section.AddParagraph();
        p.Format.Shading.Color = Panel;
        p.Format.Font.Size = 10;
        p.Format.LeftIndent = Unit.FromPoint(6);
        p.Format.RightIndent = Unit.FromPoint(6);
        p.Format.SpaceBefore = Unit.FromPoint(2);
        p.Format.SpaceAfter = Unit.FromPoint(10);
        p.AddFormattedText($"{report.Inbound.Count}", TextFormat.Bold);
        p.AddText(" inbound rules    ");
        p.AddFormattedText($"{report.Outbound.Count}", TextFormat.Bold);
        p.AddText(" outbound rules    ");
        var internet = p.AddFormattedText(
            $"{report.InternetInboundCount + report.InternetOutboundCount}", TextFormat.Bold);
        internet.Color = new Color(180, 35, 24);
        p.AddText(" internet-facing flows");
    }

    private static void AddInboundTable(Section section, FirewallReport report)
    {
        AddSectionTitle(section, "Inbound rules — allow traffic TO this machine");
        if (report.Inbound.Count == 0)
        {
            section.AddParagraph("No inbound traffic or listening ports observed.").Format.Font.Italic = true;
            return;
        }

        var table = NewTable(section);
        double[] widths = { 1.5, 2.4, 3.1, 8.6, 2.7, 2.7, 1.6 };
        foreach (var w in widths) table.AddColumn(Unit.FromCentimeter(w));
        HeaderRow(table, "Proto", "Port", "Service", "Allowed sources", "First seen", "Last seen", "Count");

        foreach (var r in report.Inbound)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(r.Protocol.ToString());
            row.Cells[1].AddParagraph(KnownPorts.Describe(r.LocalPort));
            row.Cells[2].AddParagraph(r.Process);
            if (!string.IsNullOrEmpty(r.Note))
            {
                var note = row.Cells[2].AddParagraph(r.Note);
                note.Format.Font.Size = 7; note.Format.Font.Color = Muted;
            }
            FillSources(row.Cells[3], r);
            row.Cells[4].AddParagraph(Short(r.FirstSeen));
            row.Cells[5].AddParagraph(Short(r.LastSeen));
            row.Cells[6].AddParagraph(r.ObservedTraffic ? r.Occurrences.ToString() : "0");
        }
    }

    private static void FillSources(Cell cell, InboundRule rule)
    {
        if (!rule.ObservedTraffic || rule.Sources.Count == 0)
        {
            var np = cell.AddParagraph("listening — no inbound observed");
            np.Format.Font.Italic = true;
            np.Format.Font.Color = Muted;
            return;
        }
        var p = cell.AddParagraph();
        int shown = 0;
        foreach (var s in rule.Sources.OrderByDescending(x => x.Scope).Take(12))
        {
            if (shown++ > 0) p.AddLineBreak();
            p.AddText(s.Address);
            var tag = p.AddFormattedText($"  [{IpClassifier.Label(s.Scope)}]");
            tag.Color = s.Scope == IpScope.Public ? new Color(180, 35, 24) : Muted;
            tag.Size = 7.5;
            if (!string.IsNullOrEmpty(s.Host)) { var h = p.AddText($"  {s.Host}"); }
        }
        if (rule.Sources.Count > 12)
        {
            p.AddLineBreak();
            var more = p.AddText($"(+{rule.Sources.Count - 12} more)");
        }
    }

    private static void AddOutboundTable(Section section, FirewallReport report)
    {
        AddSectionTitle(section, "Outbound rules — allow traffic FROM this machine");
        if (report.Outbound.Count == 0)
        {
            section.AddParagraph("No outbound connections observed.").Format.Font.Italic = true;
            return;
        }

        var table = NewTable(section);
        double[] widths = { 1.5, 4.2, 2.2, 3.0, 1.9, 4.2, 2.7, 1.6 };
        foreach (var w in widths) table.AddColumn(Unit.FromCentimeter(w));
        HeaderRow(table, "Proto", "Destination", "Port", "Process", "Scope", "Resolved host", "Last seen", "Count");

        foreach (var o in report.Outbound)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph(o.Protocol.ToString());
            row.Cells[1].AddParagraph(o.RemoteAddress);
            row.Cells[2].AddParagraph(KnownPorts.Describe(o.RemotePort));
            row.Cells[3].AddParagraph(o.Process);
            if (!string.IsNullOrEmpty(o.Note))
            {
                var onote = row.Cells[3].AddParagraph(o.Note);
                onote.Format.Font.Size = 7; onote.Format.Font.Color = Muted;
            }
            var scopeCell = row.Cells[4].AddParagraph(IpClassifier.Label(o.Scope));
            if (o.Scope == IpScope.Public) scopeCell.Format.Font.Color = new Color(180, 35, 24);
            row.Cells[5].AddParagraph(o.RemoteHost ?? "");
            row.Cells[6].AddParagraph(Short(o.LastSeen));
            row.Cells[7].AddParagraph(o.Occurrences.ToString());
        }
    }

    private static void AddCaveats(Section section)
    {
        AddSectionTitle(section, "Notes & caveats");
        var p = section.AddParagraph();
        p.Format.Font.Size = 8.5;
        p.Format.Font.Color = Muted;
        string[] notes =
        {
            "Rules reflect traffic OBSERVED during the window above. Rare or periodic flows may be missed — scan for a representative period (including nightly and month-end peaks) before finalizing rules.",
            "Collection combines periodic polling with ETW event capture where available. If the collector ran without elevation, ETW is disabled and connections shorter than the sampling interval may be absent.",
            "Ephemeral client ports are intentionally excluded; inbound rules key on the service (local) port, outbound rules on the destination port.",
            "Outbound internet destinations are listed by IP. IPs can change — prefer FQDN-based egress rules where your platform supports them.",
            "\"Internet\" means a routable public address; \"Private\" covers RFC1918, CGNAT, loopback and link-local."
        };
        foreach (var n in notes) { p.AddText("•  " + n); p.AddLineBreak(); }
    }

    private static void AddFooter(Section section)
    {
        var f = section.Footers.Primary.AddParagraph();
        f.Format.Font.Size = 8;
        f.Format.Font.Color = Muted;
        f.Format.Alignment = ParagraphAlignment.Center;
        f.AddText("Carrier DependenSee — See what connects.");
    }

    // ---- helpers ----

    private static void AddSectionTitle(Section section, string text)
    {
        var p = section.AddParagraph(text);
        p.Format.Font.Size = 12;
        p.Format.Font.Bold = true;
        p.Format.Font.Color = Ink;
        p.Format.SpaceBefore = Unit.FromPoint(10);
        p.Format.SpaceAfter = Unit.FromPoint(4);
        p.Format.Borders.Bottom = new Border { Width = 0.75, Color = Accent };
    }

    private static Table NewTable(Section section)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.25;
        table.Borders.Color = new Color(210, 214, 220);
        table.Format.Font.Size = 8.5;
        table.Rows.LeftIndent = 0;
        return table;
    }

    private static void HeaderRow(Table table, params string[] headers)
    {
        var row = table.AddRow();
        row.HeadingFormat = true;
        row.Shading.Color = HeaderBg;
        row.Format.Font.Bold = true;
        row.Format.Font.Color = Colors.White;
        for (int i = 0; i < headers.Length; i++)
            row.Cells[i].AddParagraph(headers[i]);
    }

    private static void AddLabelled(Paragraph p, string label, string value)
    {
        var l = p.AddFormattedText(label + ": ", TextFormat.Bold);
        l.Color = Ink;
        p.AddText(value);
    }

    /// <summary>
    /// Human-readable observation density, so a report from an hourly remote
    /// scan is never mistaken for one from 5-second local collection.
    /// </summary>
    private static string FormatCoverage(FirewallReport report)
    {
        var source = report.CollectionSource switch
        {
            "local-collector" => "local collector (continuous)",
            "remote-scan" => "remote scan (agentless snapshots)",
            _ => "unknown source"
        };
        if (report.SweepCount <= 0) return source;

        var text = $"{report.SweepCount:N0} sweeps via {source}";
        if (report.SweepCount > 1 && report.WindowStart is { } ws && report.WindowEnd is { } we && we > ws)
        {
            var avg = TimeSpan.FromTicks((we - ws).Ticks / (report.SweepCount - 1));
            var cadence = avg.TotalSeconds < 90 ? $"{avg.TotalSeconds:F0} s"
                        : avg.TotalMinutes < 90 ? $"{avg.TotalMinutes:F0} min"
                        : $"{avg.TotalHours:F1} h";
            text += $", avg one per {cadence}";
        }
        return text;
    }

    private static string FormatWindow(FirewallReport report)
    {
        if (report.WindowStart is null || report.WindowEnd is null) return "(no data)";
        return $"{report.WindowStart.Value.ToLocalTime():yyyy-MM-dd HH:mm} → " +
               $"{report.WindowEnd.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    private static string Short(DateTime dt) =>
        dt == default ? "" : dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
