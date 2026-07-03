using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ServiceMap.App.Controls;

/// <summary>
/// Lightweight node-graph renderer drawn directly with the Avalonia
/// DrawingContext — no external graph library. Nodes carry normalized (0..1)
/// coordinates computed by the view model; this control scales and paints them.
/// </summary>
public sealed class DependencyMapControl : Control
{
    public static readonly DirectProperty<DependencyMapControl, IReadOnlyList<GraphNode>?> NodesProperty =
        AvaloniaProperty.RegisterDirect<DependencyMapControl, IReadOnlyList<GraphNode>?>(
            nameof(Nodes), o => o.Nodes, (o, v) => o.Nodes = v);

    public static readonly DirectProperty<DependencyMapControl, IReadOnlyList<GraphEdge>?> EdgesProperty =
        AvaloniaProperty.RegisterDirect<DependencyMapControl, IReadOnlyList<GraphEdge>?>(
            nameof(Edges), o => o.Edges, (o, v) => o.Edges = v);

    private IReadOnlyList<GraphNode>? _nodes;
    private IReadOnlyList<GraphEdge>? _edges;

    public IReadOnlyList<GraphNode>? Nodes
    {
        get => _nodes;
        set { SetAndRaise(NodesProperty, ref _nodes, value); InvalidateVisual(); }
    }

    public IReadOnlyList<GraphEdge>? Edges
    {
        get => _edges;
        set { SetAndRaise(EdgesProperty, ref _edges, value); InvalidateVisual(); }
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 10 || h < 10) return;

        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFC)), new Rect(0, 0, w, h));

        const double pad = 60;
        Point Pos(GraphNode n) => new(pad + n.X * (w - 2 * pad), pad + n.Y * (h - 2 * pad));

        var nodes = _nodes;
        var edges = _edges;
        if (nodes is null || nodes.Count == 0)
        {
            DrawCentered(ctx, "No data to map yet — pick a mode and click Refresh.", w, h);
            return;
        }

        var byId = new Dictionary<string, GraphNode>();
        foreach (var n in nodes) byId[n.Id] = n;

        // Edges first so nodes sit on top.
        if (edges is not null)
        {
            foreach (var e in edges)
            {
                if (!byId.TryGetValue(e.FromId, out var a) || !byId.TryGetValue(e.ToId, out var b)) continue;
                var pen = new Pen(new SolidColorBrush(e.Color), e.Width);
                ctx.DrawLine(pen, Pos(a), Pos(b));
            }
        }

        var typeface = new Typeface(FontFamily.Default);
        foreach (var n in nodes)
        {
            var p = Pos(n);
            var fill = new SolidColorBrush(n.Fill);
            var stroke = new Pen(new SolidColorBrush(n.Stroke), 1.5);
            ctx.DrawEllipse(fill, stroke, p, n.Radius, n.Radius);

            var label = new FormattedText(n.Label, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 11, Brushes.Black)
            { TextAlignment = TextAlignment.Center, MaxTextWidth = 150 };
            ctx.DrawText(label, new Point(p.X - label.Width / 2, p.Y + n.Radius + 3));

            if (!string.IsNullOrEmpty(n.SubLabel))
            {
                var sub = new FormattedText(n.SubLabel, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, typeface, 9, new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)))
                { TextAlignment = TextAlignment.Center, MaxTextWidth = 150 };
                ctx.DrawText(sub, new Point(p.X - sub.Width / 2, p.Y + n.Radius + 18));
            }
        }
    }

    private static void DrawCentered(DrawingContext ctx, string text, double w, double h)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), 13, new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)));
        ctx.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
    }
}
