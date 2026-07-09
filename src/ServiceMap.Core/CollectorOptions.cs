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

    /// <summary>
    /// Aggregated flows (connection_flows) older than this are pruned. Flows are
    /// tiny (one row per distinct dependency), so this can be long.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Raw per-sweep samples older than this are pruned. Raw rows dominate
    /// database size (one row per connection per sweep); the flow table keeps
    /// the migration-relevant view far longer. Clamped to RetentionDays.
    /// </summary>
    public int RawRetentionDays { get; set; } = 7;

    /// <summary>
    /// Capture connection events via ETW (Windows) in addition to polling, so
    /// short-lived flows between sweeps are recorded. Requires elevation;
    /// silently falls back to polling only when unavailable.
    /// </summary>
    public bool EventCaptureEnabled { get; set; } = true;

    /// <summary>How often the retention prune runs.</summary>
    public int RetentionSweepMinutes { get; set; } = 60;

    /// <summary>Directory for scheduled CSV/JSON exports.</summary>
    public string ExportDirectory { get; set; } =
        DefaultDataDir("exports");

    /// <summary>Enable periodic automatic export of the last day's samples.</summary>
    public bool AutoExportEnabled { get; set; } = false;

    /// <summary>Drop high-volume UDP discovery/multicast chatter (SSDP, mDNS, etc.) before storing.</summary>
    public bool FilterDiscoveryNoise { get; set; } = true;

    /// <summary>How often the automatic export runs, when enabled.</summary>
    public int AutoExportIntervalMinutes { get; set; } = 1440;

    private static string DefaultDataDir(string leaf)
    {
        // CommonApplicationData => C:\ProgramData on Windows, /usr/share on Linux.
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(root, "CarrierDependenSee", leaf);
    }
}
