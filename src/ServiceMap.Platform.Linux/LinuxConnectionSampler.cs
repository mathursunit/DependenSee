using System.Net;
using ServiceMap.Core.Models;
using ServiceMap.Platform.Abstractions;

namespace ServiceMap.Platform.Linux;

/// <summary>
/// Linux connection sampler backed by /proc/net. Functional for endpoint and
/// state discovery; process attribution is best-effort (full coverage needs
/// root). This is the port target that mirrors the Windows sampler's behavior.
/// </summary>
public sealed class LinuxConnectionSampler : IConnectionSampler
{
    public IReadOnlyList<ConnectionSample> Sample()
    {
        var timestamp = DateTime.UtcNow;
        var resolver = new SocketInodeResolver();
        var results = new List<ConnectionSample>();

        var rows = new List<ProcNetParser.Row>();
        rows.AddRange(ProcNetParser.ParseTcp("/proc/net/tcp", ipv6: false));
        rows.AddRange(ProcNetParser.ParseTcp("/proc/net/tcp6", ipv6: true));
        rows.AddRange(ProcNetParser.ParseUdp("/proc/net/udp", ipv6: false));
        rows.AddRange(ProcNetParser.ParseUdp("/proc/net/udp6", ipv6: true));

        var listeningPorts = new HashSet<int>();
        foreach (var r in rows)
            if (r.Protocol == Protocol.Tcp && r.State == TcpState.Listen)
                listeningPorts.Add(r.LocalPort);

        foreach (var r in rows)
        {
            var (pid, name, path) = resolver.Resolve(r.Inode);

            ConnectionDirection direction;
            if (r.Protocol == Protocol.Udp || r.State == TcpState.Listen)
                direction = ConnectionDirection.Listen;
            else
                direction = listeningPorts.Contains(r.LocalPort)
                    ? ConnectionDirection.Inbound
                    : ConnectionDirection.Outbound;

            bool isListenerOnly = r.State == TcpState.Listen || r.Protocol == Protocol.Udp;

            results.Add(new ConnectionSample
            {
                Protocol = r.Protocol,
                LocalAddress = r.LocalAddress.ToString(),
                LocalPort = r.LocalPort,
                RemoteAddress = isListenerOnly ? string.Empty : r.RemoteAddress.ToString(),
                RemotePort = isListenerOnly ? 0 : r.RemotePort,
                State = r.State,
                Direction = direction,
                ProcessId = pid,
                ProcessName = name,
                ProcessPath = path,
                Timestamp = timestamp
            });
        }

        return results;
    }
}
