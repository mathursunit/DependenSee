using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ServiceMap.Core.Models;

namespace ServiceMap.Platform.Linux;

/// <summary>
/// Parses the kernel's /proc/net/{tcp,tcp6,udp,udp6} tables into endpoint rows.
/// Address and port fields are hex-encoded; IPv4/IPv6 addresses are little-endian
/// byte order, ports are in host order after hex decoding.
/// </summary>
internal static class ProcNetParser
{
    internal readonly record struct Row(
        Protocol Protocol,
        IPAddress LocalAddress, int LocalPort,
        IPAddress RemoteAddress, int RemotePort,
        TcpState State, long Inode);

    public static IEnumerable<Row> ParseTcp(string path, bool ipv6)
    {
        foreach (var line in ReadDataLines(path))
        {
            var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 10) continue;

            var (la, lp) = ParseEndpoint(cols[1], ipv6);
            var (ra, rp) = ParseEndpoint(cols[2], ipv6);
            var state = MapTcpState(cols[3]);
            long inode = ParseLong(cols[9]);

            yield return new Row(Protocol.Tcp, la, lp, ra, rp, state, inode);
        }
    }

    public static IEnumerable<Row> ParseUdp(string path, bool ipv6)
    {
        foreach (var line in ReadDataLines(path))
        {
            var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 10) continue;

            var (la, lp) = ParseEndpoint(cols[1], ipv6);
            long inode = ParseLong(cols[9]);

            yield return new Row(Protocol.Udp, la, lp, IPAddress.None, 0,
                TcpState.Unknown, inode);
        }
    }

    private static IEnumerable<string> ReadDataLines(string path)
    {
        if (!File.Exists(path)) yield break;
        // First line is the header.
        bool first = true;
        foreach (var line in File.ReadLines(path))
        {
            if (first) { first = false; continue; }
            if (!string.IsNullOrWhiteSpace(line)) yield return line.Trim();
        }
    }

    private static (IPAddress, int) ParseEndpoint(string field, bool ipv6)
    {
        int colon = field.IndexOf(':');
        if (colon < 0) return (IPAddress.None, 0);

        string addrHex = field[..colon];
        string portHex = field[(colon + 1)..];
        int port = int.Parse(portHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return (ipv6 ? ParseIpv6(addrHex) : ParseIpv4(addrHex), port);
    }

    private static IPAddress ParseIpv4(string hex)
    {
        // 8 hex chars, little-endian.
        uint raw = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        // IPAddress(uint) expects network order already matching the little-endian layout.
        return new IPAddress(raw);
    }

    private static IPAddress ParseIpv6(string hex)
    {
        // 32 hex chars = 16 bytes, stored as four little-endian 32-bit words.
        var bytes = new byte[16];
        for (int i = 0; i < 16; i++)
            bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        try { return new IPAddress(bytes); }
        catch { return IPAddress.IPv6None; }
    }

    private static long ParseLong(string s) =>
        long.TryParse(s, out var v) ? v : 0;

    private static TcpState MapTcpState(string hex) => hex.ToUpperInvariant() switch
    {
        "01" => TcpState.Established,
        "02" => TcpState.SynSent,
        "03" => TcpState.SynReceived,
        "04" => TcpState.FinWait1,
        "05" => TcpState.FinWait2,
        "06" => TcpState.TimeWait,
        "07" => TcpState.Closed,
        "08" => TcpState.CloseWait,
        "09" => TcpState.LastAck,
        "0A" => TcpState.Listen,
        "0B" => TcpState.Closing,
        _ => TcpState.Unknown
    };
}
