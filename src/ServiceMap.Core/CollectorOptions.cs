namespace ServiceMap.Core;

/// <summary>
/// Runtime configuration shared by the collector service and the GUI reader.
/// Bound from appsettings.json (collector) and defaulted for the GUI.
/// </summary>
public sealed class CollectorOptions
{
    public const string SectionName = "Collector";

    /// <summary>
    /// Full path to the SQLite database. Defaults to a machine-wide location
    /// under ProgramData so the service (writer) and GUI (reader) share it.
    /// </summary>
    public string DatabasePath { get; set; } =
        DefaultDataDir("servicemap.db");

    /// <summary>How often to sample active connections.</summary>
    public int SamplingIntervalSeconds { get; set; } = 5;

    /// <summary>How often to snapshot the registered service list.</summary>
    public int ServiceScanIntervalSeconds { get; set; } = 60;

    /// <summary>Connection samples older than this are pruned.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>How often the retention prune runs.</summary>
    public int RetentionSweepMinutes { get; set; } = 60;

    /// <summary>Directory for scheduled CSV/JSON exports.</summary>
    public string ExportDirectory { get; set; } =
        DefaultDataDir("exports");

    /// <summary>Enable periodic automatic export of the last day's samples.</summary>
    public bool AutoExportEnabled { get; set; } = false;

    /// <summary>How often the automatic export runs, when enabled.</summary>
    public int AutoExportIntervalMinutes { get; set; } = 1440;

    private static string DefaultDataDir(string leaf)
    {
        // CommonApplicationData => C:\ProgramData on Windows, /usr/share on Linux.
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(root, "CarrierDependenSee", leaf);
    }
}
