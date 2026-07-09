using ServiceMap.Core.Models;
using ServiceMap.Core.Net;
using ServiceMap.Platform.Abstractions;
using ServiceMap.Platform.Windows.Interop;

namespace ServiceMap.Platform.Windows;

/// <summary>
/// Samples TCP connections and TCP/UDP listeners via iphlpapi, attributing each
/// to its owning process. Direction is inferred from the set of local ports the
/// host has recently been listening on — a <see cref="ListenPortTracker"/> keeps
/// listen knowledge across sweeps so a listener that restarts (or races the
/// snapshot) doesn't flip its established connections to outbound.
/// </summary>
public sealed class WindowsConnectionSampler : IConnectionSampler
{
    private readonly ListenPortTracker _listenTracker = new();

    public IReadOnlyList<ConnectionSample> Sample()
    {
        var timestamp = DateTime.UtcNow;
        var processes = new ProcessInfoCache();
        var results = new List<ConnectionSample>();

        var tcp = IpHlpApi.GetTcpConnections();

        // Record every port observed listening this sweep; the tracker keeps
        // ports from recent sweeps alive so inference survives restarts.
        foreach (var row in tcp)
        {
            if ((TcpState)row.State == TcpState.Listen)
                _listenTracker.Observe(row.LocalPort, timestamp);
        }
        _listenTracker.Sweep(timestamp);

        foreach (var row in tcp)
        {
            var state = (TcpState)row.State;
            var (name, path) = processes.Resolve(row.ProcessId);

            var direction = DirectionClassifier.Classify(
                Protocol.Tcp, state, row.LocalPort,
                p => _listenTracker.IsListening(p, timestamp));

            results.Add(new ConnectionSample
            {
                Protocol = Protocol.Tcp,
                LocalAddress = row.LocalAddress.ToString(),
                LocalPort = row.LocalPort,
                RemoteAddress = state == TcpState.Listen ? string.Empty : row.RemoteAddress.ToString(),
                RemotePort = state == TcpState.Listen ? 0 : row.RemotePort,
                State = state,
                Direction = direction,
                ProcessId = row.ProcessId,
                ProcessName = name,
                ProcessPath = path,
                Timestamp = timestamp
            });
        }

        // UDP is connectionless; we can only record the listening endpoints.
        foreach (var row in IpHlpApi.GetUdpListeners())
        {
            var (name, path) = processes.Resolve(row.ProcessId);
            results.Add(new ConnectionSample
            {
                Protocol = Protocol.Udp,
                LocalAddress = row.LocalAddress.ToString(),
                LocalPort = row.LocalPort,
                RemoteAddress = string.Empty,
                RemotePort = 0,
                State = TcpState.Unknown,
                Direction = ConnectionDirection.Listen,
                ProcessId = row.ProcessId,
                ProcessName = name,
                ProcessPath = path,
                Timestamp = timestamp
            });
        }

        return results;
    }
}
