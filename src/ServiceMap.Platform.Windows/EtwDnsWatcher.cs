using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using ServiceMap.Core.Models;
using ServiceMap.Platform.Abstractions;

namespace ServiceMap.Platform.Windows;

/// <summary>
/// Captures DNS query completions via the Microsoft-Windows-DNS-Client ETW
/// provider (event id 3008 carries the queried name and the resolved answer
/// list). Folds to distinct (process, name, IP). Requires elevation; Start()
/// never throws — falls back silently when the session can't be opened.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EtwDnsWatcher : IDnsWatcher
{
    private const string SessionName = "CarrierDependenSee-Dns";
    private static readonly Guid DnsClientProvider = new("1c95126e-7eea-49a9-a3fe-a378b03ddb4d");
    private const int MaxBuffered = 200_000;

    private readonly object _gate = new();
    private Dictionary<(string, string, string), DnsResolution> _buffer = new();
    private TraceEventSession? _session;
    private Thread? _thread;
    private volatile bool _running;
    private readonly ProcessInfoCache _procs = new();

    public bool IsRunning => _running;
    public string? LastError { get; private set; }

    public void Start()
    {
        if (_running) return;
        try
        {
            _session = new TraceEventSession(SessionName) { StopOnDispose = true };
            _session.EnableProvider(DnsClientProvider);
            _session.Source.Dynamic.All += OnEvent;
            _thread = new Thread(() =>
            {
                try { _session?.Source.Process(); }
                catch (Exception ex) { LastError = ex.Message; }
                finally { _running = false; }
            }) { IsBackground = true, Name = "EtwDnsWatcher" };
            _running = true;
            _thread.Start();
        }
        catch (Exception ex)
        {
            LastError = ex is UnauthorizedAccessException
                ? "DNS capture requires elevation." : ex.Message;
            _running = false;
            _session?.Dispose();
            _session = null;
        }
    }

    private void OnEvent(TraceEvent data)
    {
        // 3008 = DNS query completed. QueryName + QueryResults (semicolon list).
        if ((int)data.ID != 3008) return;
        var name = data.PayloadByName("QueryName") as string;
        var results = data.PayloadByName("QueryResults") as string;
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(results)) return;

        var (proc, _) = _procs.Resolve(data.ProcessID);
        var now = DateTime.UtcNow;
        // QueryResults e.g. "type: 5 ...;::ffff:10.1.2.3;10.1.2.3" — keep IP-looking tokens.
        foreach (var tok in results.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!System.Net.IPAddress.TryParse(tok, out var ip)) continue;
            var addr = ip.ToString();
            var key = (proc, name!, addr);
            lock (_gate)
            {
                if (_buffer.TryGetValue(key, out var e)) { e.LastSeen = now; e.Count++; }
                else if (_buffer.Count < MaxBuffered)
                    _buffer[key] = new DnsResolution
                    {
                        ProcessName = proc, QueryName = name!, ResolvedAddress = addr,
                        FirstSeen = now, LastSeen = now, Count = 1
                    };
            }
        }
    }

    public IReadOnlyList<DnsResolution> Drain()
    {
        lock (_gate)
        {
            if (_buffer.Count == 0) return Array.Empty<DnsResolution>();
            var drained = _buffer.Values.ToList();
            _buffer = new Dictionary<(string, string, string), DnsResolution>();
            return drained;
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _session?.Dispose(); } catch { }
        _session = null;
    }
}
