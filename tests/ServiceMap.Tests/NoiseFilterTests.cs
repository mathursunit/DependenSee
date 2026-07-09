using ServiceMap.Core.Models;
using ServiceMap.Core.Net;
using Xunit;

namespace ServiceMap.Tests;

public class NoiseFilterTests
{
    private static ConnectionSample Udp(int localPort, string localAddr = "10.0.0.5",
        string remoteAddr = "", int remotePort = 0,
        ConnectionDirection dir = ConnectionDirection.Listen) => new()
    {
        Protocol = Protocol.Udp,
        LocalAddress = localAddr,
        LocalPort = localPort,
        RemoteAddress = remoteAddr,
        RemotePort = remotePort,
        Direction = dir
    };

    [Theory]
    [InlineData(1900)]  // SSDP
    [InlineData(5353)]  // mDNS
    [InlineData(5355)]  // LLMNR
    [InlineData(3702)]  // WS-Discovery
    [InlineData(137)]   // NetBIOS-NS
    [InlineData(138)]   // NetBIOS-DGM
    public void DiscoveryPortsAreNoise(int port) =>
        Assert.True(NoiseFilter.IsNoise(Udp(port)));

    [Fact]
    public void TcpIsNeverNoise()
    {
        var s = new ConnectionSample
        {
            Protocol = Protocol.Tcp,
            LocalPort = 1900,                       // even on a discovery port
            RemoteAddress = "239.255.255.250"       // even to a multicast group
        };
        Assert.False(NoiseFilter.IsNoise(s));
    }

    [Fact]
    public void EphemeralUdpListenersAreNoise() =>
        Assert.True(NoiseFilter.IsNoise(Udp(60321)));

    [Fact]
    public void WellKnownUdpListenerIsKept() =>
        Assert.False(NoiseFilter.IsNoise(Udp(53)));   // DNS server

    [Theory]
    [InlineData("239.255.255.250")]  // IPv4 multicast
    [InlineData("224.0.0.251")]
    [InlineData("255.255.255.255")]  // broadcast
    [InlineData("ff02::fb")]         // IPv6 multicast
    public void MulticastAndBroadcastAreNoise(string remote) =>
        Assert.True(NoiseFilter.IsNoise(Udp(5000, remoteAddr: remote, remotePort: 5000)));

    [Fact]
    public void OrdinaryUdpFlowIsKept() =>
        Assert.False(NoiseFilter.IsNoise(
            Udp(5000, remoteAddr: "10.1.2.3", remotePort: 514, dir: ConnectionDirection.Outbound)));
}
