using PdfSharp.Fonts;

namespace ServiceMap.Reporting;

/// <summary>
/// Minimal cross-platform font resolver for the PDFsharp "core" build (which has
/// no automatic font resolution). Maps every requested family to a sans-serif
/// face loaded from well-known OS font locations — Arial on Windows, DejaVu/
/// Liberation on Linux — so the report renders on the Windows target and in CI.
/// </summary>
public sealed class ReportFontResolver : IFontResolver
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, byte[]> Cache = new();

    private static readonly string[] Regular =
    {
        @"C:\Windows\Fonts\arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans.ttf",
        "/Library/Fonts/Arial.ttf"
    };
    private static readonly string[] Bold =
    {
        @"C:\Windows\Fonts\arialbd.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf"
    };
    private static readonly string[] Italic =
    {
        @"C:\Windows\Fonts\ariali.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Italic.ttf"
    };
    private static readonly string[] BoldItalic =
    {
        @"C:\Windows\Fonts\arialbi.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-BoldOblique.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-BoldItalic.ttf"
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var face = (isBold, isItalic) switch
        {
            (true, true) => "sans-bi",
            (true, false) => "sans-b",
            (false, true) => "sans-i",
            _ => "sans"
        };
        EnsureLoaded(face);
        // Fall back to regular if the styled face wasn't found on this box.
        if (!Cache.ContainsKey(face)) face = "sans";
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName)
    {
        EnsureLoaded(faceName);
        return Cache.TryGetValue(faceName, out var bytes) ? bytes : null;
    }

    private static void EnsureLoaded(string face)
    {
        lock (Gate)
        {
            if (Cache.ContainsKey(face)) return;
            var candidates = face switch
            {
                "sans-b" => Bold,
                "sans-i" => Italic,
                "sans-bi" => BoldItalic,
                _ => Regular
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    Cache[face] = File.ReadAllBytes(path);
                    return;
                }
            }
            // Last resort: reuse regular bytes if we have them.
            if (face != "sans" && Cache.TryGetValue("sans", out var reg))
                Cache[face] = reg;
        }
    }
}
