using System.Globalization;
using Avalonia.Data.Converters;

namespace ServiceMap.App.ViewModels;

/// <summary>Formats a stored UTC <see cref="DateTime"/> as a local short date/time.</summary>
public sealed class LocalTimeConverter : IValueConverter
{
    public static readonly LocalTimeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
