using System.Net;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using ServiceMap.Core.Models;
using ServiceMap.Platform.Abstractions;

namespace ServiceMap.Platform.Windows;

/// <summary>
/// ETW-based capture of TCP connect/accept and outbound UDP send events via the
/// kernel network provider. Polling the connection table every few seconds
/// misses flows that open and close between sweeps; this watcher sees every
/// establishment the kernel reports, giving the firewall report the short-lived
/// dependencies (DNS, quick RPC/API calls, SQL logins) a poll can't.
///
/// Direction is definitive here: a Connect event is outbound, an Accept event is
/// inbound — no listen-port inference involved.
///
/// Requires elevation (ETW kernel sessions are admin-only). <see cref="Start"/>
/// never throws; when the session can't be opened the watcher reports the cause
/// through <see cref="LastError"/> and the collector falls back to polling only.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EtwConnectionWatcher : IConnectionEventWatcher
{
    private const string SessionName = "CarrierDependenSee-Network";
    private const int MaxBufferedFlows = 100_000;

    private readonly object _gate = new();
    private Dictionary<FlowKey, ConnectionSample> _buffer = new();

    private TraceEventSession? _session;
    private Thread? _thread;
    private volatile bool _running;

    public bool IsRunning => _running;
    public string? LastError { get; private set; }

    private readonly record struct FlowKey(
        Protocol Protocol, ConnectionDirection Direction, int Pid,
        string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort);

    public void Start()
    {
        if (_running) return;
        try
        {
            // Replace any orphaned session from a previous unclean shutdown.
            _session = new TraceEventSession(SessionName)
            {
                StopOnDispose = true
            };
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            var kernel = _session.Source.Kernel;
            kernel.TcpIpConnect += d => OnTcp(d.ProcessID, d.saddr, d.sport, d.daddr, d.dport, ConnectionDirection.Outbound, d.TimeStamp);
            kernel.TcpIpAccept += d => OnTcp(d.ProcessID, d.saddr, d.sport, d.daddr, d.dport, ConnectionDirection.Inbound, d.TimeStamp);
            kernel.TcpIpConnectIPV6 += d => OnTcp(d.ProcessID, d.saddr, d.sport, d.daddr, d.dport, ConnectionDirection.Outbound, d.TimeStamp);
            kernel.TcpIpAcceptIPV6 += d => OnTcp(d.ProcessID, d.saddr, d.sport, d.daddr, d.dport, ConnectionDirection.Inbound, d.TimeStamp);
            // Outbound UDP (e.g. DNS queries) — invisible to the polling sampler,
            // which can only see UDP listeners.
            kernel.UdpIpSend += d => OnUdp(d.ProcessID, d.saddr, d.sport, d.daddr, d.dport, d.TimeStamp);
            kernel.UdpIpSendIPV6 += d => OnUdp(d.ProcessID, d.saddr, d.sport, d.daddr, d.dport, d.TimeStamp);

            _thread = new Thread(ProcessLoop)
            {
                IsBackground = true,
                Name = "EtwConnectionWatcher"
            };
            _running = true;
            _thread.Start();
        }
        catch (Exception ex)
        {
            LastError = ex is UnauthorizedAccessException
                ? "ETW capture requires elevation; running with polling only."
                : ex.Message;
            _running = false;
            _session?.Dispose();
            _session = null;
        }
    }

    private void ProcessLoop()
    {
        try
        {
            _session?.Source.Process();   // blocks until the session stops
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            _running = false;
        }
    }

    private void OnTcp(int pid, IPAddress saddr, int sport, IPAddress daddr, int dport,
        ConnectionDirection direction, DateTime timestamp) =>
        Record(Protocol.Tcp, TcpState.Established, pid, saddr, sport, daddr, dport, direction, timestamp);

    private void OnUdp(int pid, IPAddress saddr, int sport, IPAddress daddr, int dport, DateTime timestamp) =>
        Record(Protocol.Udp, TcpState.Unknown, pid, saddr, sport, daddr, dport, ConnectionDirection.Outbound, timestamp);

    private void Record(Protocol proto, TcpState state, int pid,
        IPAddress saddr, int sport, IPAddress daddr, int dport,
        ConnectionDirection direction, DateTime timestamp)
    {
        var local = saddr.ToString();
        var remote = daddr.ToString();
        var key = new FlowKey(proto, direction, pid, local, sport, remote, dport);

        lock (_gate)
        {
            if (_buffer.TryGetValue(key, out var existing))
            {
                existing.Timestamp = timestamp.ToUniversalTime();
                return;
            }
            if (_buffer.Count >= MaxBufferedFlows) return;   // safety valve
            _buffer[key] = new ConnectionSample
            {
                Protocol = proto,
                LocalAddress = local,
                LocalPort = sport,
                RemoteAddress = remote,
                RemotePort = dport,
                State = state,
                Direction = direction,
                ProcessId = pid,
                Timestamp = timestamp.ToUniversalTime()
            };
        }
    }

    public IReadOnlyList<ConnectionSample> Drain()
    {
        Dictionary<FlowKey, ConnectionSample> drained;
        lock (_gate)
        {
            if (_buffer.Count == 0) return Array.Empty<ConnectionSample>();
            drained = _buffer;
            _buffer = new Dictionary<FlowKey, ConnectionSample>();
        }

        // Resolve process names once per drain; ETW events carry only the PID.
        var processes = new ProcessInfoCache();
        var list = new List<ConnectionSample>(drained.Count);
        foreach (var s in drained.Values)
        {
            var (name, path) = processes.Resolve(s.ProcessId);
            s.ProcessName = name;
            s.ProcessPath = path;
            list.Add(s);
        }
        return list;
    }

    public void Dispose()
    {
        _running = false;
        try { _session?.Dispose(); } catch { /* stopping an ETW session is best-effort */ }
        _session = null;
    }
}
