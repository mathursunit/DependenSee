using ServiceMap.Core.Analysis;
using ServiceMap.Core.Models;

namespace ServiceMap.App.Services;

/// <summary>
/// Walks the config artifacts next to a machine's service executables and
/// extracts embedded endpoints via <see cref="ConfigScavenger"/>. Only usable
/// for the LOCAL machine (it reads the real filesystem), so callers gate on
/// that. Bounded file count/size; system directories skipped.
/// </summary>
public static class ConfigScanHelper
{
    private static readonly string[] Patterns = { "*.config", "appsettings*.json", "*.env", "web.config" };
    private const int MaxFiles = 400;
    private const long MaxFileBytes = 512 * 1024;

    public static List<ConfigEndpoint> ScanLocal(IReadOnlyList<ServiceRecord> services, bool keepRaw)
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var dirs = services
            .Select(s => TryGetDir(s.ExecutablePath))
            .Where(d => d is not null && (winDir.Length == 0 || !d!.StartsWith(winDir, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        var results = new List<ConfigEndpoint>();
        var scanned = 0;
        foreach (var dir in dirs)
        {
            foreach (var pattern in Patterns)
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
                catch { continue; }
                foreach (var file in files)
                {
                    if (scanned++ > MaxFiles) return Dedupe(results);
                    try
                    {
                        if (new FileInfo(file).Length > MaxFileBytes) continue;
                        results.AddRange(ConfigScavenger.Scan(file, File.ReadAllText(file), keepRaw));
                    }
                    catch { /* unreadable */ }
                }
            }
        }
        return Dedupe(results);
    }

    private static string? TryGetDir(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        try
        {
            var path = exePath.Trim();
            if (path.StartsWith('"')) { var end = path.IndexOf('"', 1); if (end > 0) path = path[1..end]; }
            else { var sp = path.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase); if (sp > 0) path = path[..(sp + 4)]; }
            var dir = Path.GetDirectoryName(path);
            return Directory.Exists(dir) ? dir : null;
        }
        catch { return null; }
    }

    private static List<ConfigEndpoint> Dedupe(List<ConfigEndpoint> list) => list
        .GroupBy(e => (e.Host.ToLowerInvariant(), e.Port, e.Kind))
        .Select(g => g.First())
        .OrderBy(e => e.Host).ThenBy(e => e.Port)
        .ToList();
}
