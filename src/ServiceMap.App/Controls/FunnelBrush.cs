using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ServiceMap.App.Controls;

/// <summary>Colours the column funnel glyph blue when the filter is active, grey otherwise.</summary>
public sealed class FunnelBrush : IValueConverter
{
    public static readonly FunnelBrush Instance = new();
    private static readonly IBrush Active = Brushes.DodgerBlue;
    private static readonly IBrush Idle = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Active : Idle;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
