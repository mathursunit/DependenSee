using ServiceMap.Core.Models;
using ServiceMap.Core.Net;
using Xunit;

namespace ServiceMap.Tests;

public class DirectionClassifierTests
{
    private static ConnectionSample Tcp(TcpState state, int localPort, int remotePort = 0) => new()
    {
        Protocol = Protocol.Tcp,
        State = state,
        LocalAddress = "10.0.0.5",
        LocalPort = localPort,
        RemoteAddress = state == TcpState.Listen ? "" : "10.0.0.9",
        RemotePort = remotePort
    };

    [Fact]
    public void ListenersAreListen()
    {
        var s = new[] { Tcp(TcpState.Listen, 8080) };
        DirectionClassifier.AssignBatch(s);
        Assert.Equal(ConnectionDirection.Listen, s[0].Direction);
    }

    [Fact]
    public void UdpIsAlwaysListenInBatchInference()
    {
        var s = new[] { new ConnectionSample { Protocol = Protocol.Udp, LocalPort = 53 } };
        DirectionClassifier.AssignBatch(s);
        Assert.Equal(ConnectionDirection.Listen, s[0].Direction);
    }

    [Fact]
    public void ConnectionToOwnListenPortIsInbound()
    {
        var s = new[]
        {
            Tcp(TcpState.Listen, 443),
            Tcp(TcpState.Established, 443, 52001)
        };
        DirectionClassifier.AssignBatch(s);
        Assert.Equal(ConnectionDirection.Inbound, s[1].Direction);
    }

    [Fact]
    public void ConnectionFromEphemeralPortIsOutbound()
    {
        var s = new[] { Tcp(TcpState.Established, 52001, 443) };
        DirectionClassifier.AssignBatch(s);
        Assert.Equal(ConnectionDirection.Outbound, s[0].Direction);
    }

    [Fact]
    public void ListenerOnHighPortStillYieldsInbound()
    {
        // A service listening in the ephemeral range must still classify its
        // connections inbound — the listen check outranks port-range heuristics.
        var s = new[]
        {
            Tcp(TcpState.Listen, 50123),
            Tcp(TcpState.Established, 50123, 41000)
        };
        DirectionClassifier.AssignBatch(s);
        Assert.Equal(ConnectionDirection.Inbound, s[1].Direction);
    }

    [Fact]
    public void TrackerKeepsPortListeningWithinTtl()
    {
        var t0 = DateTime.UtcNow;
        var tracker = new ListenPortTracker(TimeSpan.FromMinutes(15));
        tracker.Observe(8080, t0);

        // Listener disappears from later sweeps but connections keep arriving.
        Assert.True(tracker.IsListening(8080, t0.AddMinutes(5)));
        Assert.Equal(ConnectionDirection.Inbound, DirectionClassifier.Classify(
            Protocol.Tcp, TcpState.Established, 8080,
            p => tracker.IsListening(p, t0.AddMinutes(5))));
    }

    [Fact]
    public void TrackerExpiresPortAfterTtl()
    {
        var t0 = DateTime.UtcNow;
        var tracker = new ListenPortTracker(TimeSpan.FromMinutes(15));
        tracker.Observe(8080, t0);

        Assert.False(tracker.IsListening(8080, t0.AddMinutes(16)));
        Assert.Equal(ConnectionDirection.Outbound, DirectionClassifier.Classify(
            Protocol.Tcp, TcpState.Established, 8080,
            p => tracker.IsListening(p, t0.AddMinutes(16))));
    }

    [Fact]
    public void TrackerSweepRemovesStaleEntries()
    {
        var t0 = DateTime.UtcNow;
        var tracker = new ListenPortTracker(TimeSpan.FromMinutes(15));
        tracker.Observe(80, t0);
        tracker.Observe(443, t0.AddMinutes(20));

        tracker.Sweep(t0.AddMinutes(21));
        Assert.Equal(1, tracker.Count);
        Assert.True(tracker.IsListening(443, t0.AddMinutes(21)));
    }

    [Fact]
    public void ObserveRefreshesTtl()
    {
        var t0 = DateTime.UtcNow;
        var tracker = new ListenPortTracker(TimeSpan.FromMinutes(15));
        tracker.Observe(8080, t0);
        tracker.Observe(8080, t0.AddMinutes(10));
        Assert.True(tracker.IsListening(8080, t0.AddMinutes(20)));
    }
}
