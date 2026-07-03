using ServiceMap.Core.Models;
using ServiceMap.Platform.Abstractions;
using ServiceMap.Platform.Windows.Interop;

namespace ServiceMap.Platform.Windows;

/// <summary>
/// Samples TCP connections and TCP/UDP listeners via iphlpapi, attributing each
/// to its owning process. Direction is inferred from the set of local ports the
/// host is listening on in the same sweep.
/// </summary>
public sealed class WindowsConnectionSampler : IConnectionSampler
{
    public IReadOnlyList<ConnectionSample> Sample()
    {
        var timestamp = DateTime.UtcNow;
        var processes = new ProcessInfoCache();
        var results = new List<ConnectionSample>();

        var tcp = IpHlpApi.GetTcpConnections();

        // Build the set of ports this host is actively listening on so we can
        // classify established connections as inbound vs outbound.
        var listeningPorts = new HashSet<int>();
        foreach (var row in tcp)
        {
            if ((TcpState)row.State == TcpState.Listen)
                listeningPorts.Add(row.LocalPort);
        }

        foreach (var row in tcp)
        {
            var state = (TcpState)row.State;
            var (name, path) = processes.Resolve(row.ProcessId);

            ConnectionDirection direction = state switch
            {
                TcpState.Listen => ConnectionDirection.Listen,
                _ => listeningPorts.Contains(row.LocalPort)
                    ? ConnectionDirection.Inbound
                    : ConnectionDirection.Outbound
            };

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
