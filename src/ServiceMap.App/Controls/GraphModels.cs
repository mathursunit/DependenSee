using Avalonia.Media;

namespace ServiceMap.App.Controls;

/// <summary>A node in the dependency map. X/Y are normalized (0..1).</summary>
public sealed class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? SubLabel { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Radius { get; set; } = 26;
    public Color Fill { get; set; } = Colors.SteelBlue;
    public Color Stroke { get; set; } = Colors.DimGray;
}

/// <summary>A directed edge between two nodes.</summary>
public sealed class GraphEdge
{
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public Color Color { get; set; } = Color.FromArgb(120, 120, 120, 120);
    public double Width { get; set; } = 1.5;
}
