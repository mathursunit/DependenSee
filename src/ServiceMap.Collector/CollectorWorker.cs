using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceMap.Core;
using ServiceMap.Core.Export;
using ServiceMap.Core.Storage;
using ServiceMap.Engine;

namespace ServiceMap.Collector;

/// <summary>
/// Long-running background worker: samples connections on a fast cadence,
/// snapshots services on a slower cadence, prunes old data periodically, and
/// optionally auto-exports. Designed to run as a Windows Service.
/// </summary>
public sealed class CollectorWorker : BackgroundService
{
    private readonly ILogger<CollectorWorker> _log;
    private readonly CollectorOptions _options;
    private readonly SampleRepository _repository;
    private readonly CollectionEngine _engine;

    private DateTime _lastServiceScan = DateTime.MinValue;
    private DateTime _lastPrune = DateTime.MinValue;
    private DateTime _lastExport = DateTime.MinValue;
    private DateTime _lastMetric = DateTime.MinValue;
    private DateTime _lastDnsDrain = DateTime.MinValue;

    public CollectorWorker(
        ILogger<CollectorWorker> log,
        IOptions<CollectorOptions> options,
        SampleRepository repository,
        CollectionEngine engine)
    {
        _log = log;
        _options = options.Value;
        _repository = repository;
        _engine = engine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _repository.Initialize();
        _repository.SetMeta("machine_name", Environment.MachineName);
        _repository.SetMeta("collection_source", "local-collector");
        _log.LogInformation(
            "Carrier DependenSee collector started on {Platform}. Elevated={Elevated}. DB={Db}",
            _engine.PlatformName, _engine.IsElevated, _options.DatabasePath);

        if (!_engine.IsElevated)
        {
            _log.LogWarning(
                "Not running elevated. Port-to-process attribution and some service " +
                "details may be incomplete. Run the service under LocalSystem or an admin account.");
        }

        if (_engine.EventCaptureActive)
        {
            _log.LogInformation(
                "ETW event capture active: short-lived connections between sweeps will be recorded.");
        }
        else if (_options.EventCaptureEnabled)
        {
            _log.LogWarning(
                "ETW event capture unavailable ({Reason}); short-lived connections " +
                "between sweeps may be missed.", _engine.EventCaptureError ?? "unknown");
        }

        if (_options.DnsCaptureEnabled)
        {
            _engine.EnableDnsCapture();
            if (_engine.DnsCaptureActive) _log.LogInformation("DNS-Client capture active.");
            else _log.LogWarning("DNS capture unavailable ({Reason}).", _engine.DnsCaptureError ?? "unknown");
        }
        if (_options.MetricsEnabled)
        {
            _engine.EnableMetrics();
            if (_engine.MetricsActive) _log.LogInformation("Utilization sampling active.");
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.SamplingIntervalSeconds));

        // Prime the service list immediately so the GUI has data on first open.
        SafeScanServices();

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStart = DateTime.UtcNow;

            SafeSampleConnections();
            MaybeScanServices();
            MaybeSampleMetrics();
            MaybeDrainDns();
            MaybePrune();
            MaybeExport();

            var elapsed = DateTime.UtcNow - cycleStart;
            var delay = interval - elapsed;
            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        _log.LogInformation("Carrier DependenSee collector stopping.");
    }

    private void SafeSampleConnections()
    {
        try
        {
            int n = _engine.SampleConnectionsOnce();
            _log.LogDebug("Sampled {Count} connection rows.", n);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Connection sampling failed.");
        }
    }

    private void MaybeScanServices()
    {
        if ((DateTime.UtcNow - _lastServiceScan).TotalSeconds >= _options.ServiceScanIntervalSeconds)
            SafeScanServices();
    }

    private void SafeScanServices()
    {
        try
        {
            int n = _engine.ScanServicesOnce();
            _lastServiceScan = DateTime.UtcNow;
            _log.LogDebug("Snapshotted {Count} services.", n);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Service scan failed.");
        }
    }

    private void MaybeSampleMetrics()
    {
        if (!_options.MetricsEnabled) return;
        if ((DateTime.UtcNow - _lastMetric).TotalSeconds < Math.Max(5, _options.MetricsIntervalSeconds)) return;
        try { _engine.SampleMetricsOnce(); _lastMetric = DateTime.UtcNow; }
        catch (Exception ex) { _log.LogDebug(ex, "Metric sample failed."); }
    }

    private void MaybeDrainDns()
    {
        if (!_options.DnsCaptureEnabled) return;
        if ((DateTime.UtcNow - _lastDnsDrain).TotalSeconds < 30) return;
        try
        {
            var n = _engine.DrainDns();
            _lastDnsDrain = DateTime.UtcNow;
            if (n > 0) _log.LogDebug("Stored {Count} DNS resolutions.", n);
        }
        catch (Exception ex) { _log.LogDebug(ex, "DNS drain failed."); }
    }

    private void MaybePrune()
    {
        if ((DateTime.UtcNow - _lastPrune).TotalMinutes < _options.RetentionSweepMinutes)
            return;
        try
        {
            // Raw per-sweep rows are bulky and short-lived; aggregated flows are
            // tiny and keep the migration view for the full retention window.
            var rawDays = Math.Min(Math.Max(1, _options.RawRetentionDays), _options.RetentionDays);
            var rawCutoff = DateTime.UtcNow.AddDays(-rawDays);
            var flowCutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
            int removed = _repository.PruneConnectionsOlderThan(rawCutoff, flowCutoff);
            _lastPrune = DateTime.UtcNow;
            if (removed > 0)
                _log.LogInformation(
                    "Pruned {Count} raw samples older than {RawDays} days (flows kept {FlowDays} days).",
                    removed, rawDays, _options.RetentionDays);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Retention prune failed.");
        }
    }

    private void MaybeExport()
    {
        if (!_options.AutoExportEnabled) return;
        if ((DateTime.UtcNow - _lastExport).TotalMinutes < _options.AutoExportIntervalMinutes)
            return;
        try
        {
            var since = DateTime.UtcNow.AddDays(-1);
            var rows = _repository.QueryConnections(new ConnectionQuery { From = since, Limit = 1_000_000 });
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(_options.ExportDirectory, $"connections-{stamp}.csv");
            SampleExporter.WriteCsv(path, rows);
            _lastExport = DateTime.UtcNow;
            _log.LogInformation("Auto-exported {Count} rows to {Path}.", rows.Count, path);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Auto-export failed.");
        }
    }
}
