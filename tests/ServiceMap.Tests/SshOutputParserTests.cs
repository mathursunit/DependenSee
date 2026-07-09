using ServiceMap.Core.Models;
using ServiceMap.Remote.Parsing;
using Xunit;

namespace ServiceMap.Tests;

public class SshOutputParserTests
{
    private static readonly DateTime Ts = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string Output = """
        ###HOST
        webhost01
        ###SS
        tcp   LISTEN 0      511          0.0.0.0:80        0.0.0.0:*     users:(("nginx",pid=1234,fd=6))
        tcp   ESTAB  0      0         10.0.0.5:80       10.0.0.9:52100  users:(("nginx",pid=1234,fd=8))
        tcp   ESTAB  0      0         10.0.0.5:44212    10.0.0.7:5432   users:(("python3",pid=2345,fd=3))
        udp   UNCONN 0      0          0.0.0.0:123       0.0.0.0:*     users:(("chronyd",pid=890,fd=5))
        ###UNITS
        nginx.service     loaded active   running Nginx HTTP server
        postgresql.service loaded inactive dead    PostgreSQL database
        ###FILES
        nginx.service      enabled  enabled
        postgresql.service disabled disabled
        """;

    [Fact]
    public void ParsesHostname() =>
        Assert.Equal("webhost01", SshOutputParser.Parse(Output, Ts).MachineName);

    [Fact]
    public void ParsesConnectionsWithAttribution()
    {
        var parsed = SshOutputParser.Parse(Output, Ts);
        Assert.Equal(4, parsed.Connections.Count);

        var listener = parsed.Connections[0];
        Assert.Equal(Protocol.Tcp, listener.Protocol);
        Assert.Equal(TcpState.Listen, listener.State);
        Assert.Equal(80, listener.LocalPort);
        Assert.Equal("nginx", listener.ProcessName);
        Assert.Equal(1234, listener.ProcessId);
        Assert.Equal(string.Empty, listener.RemoteAddress);   // wildcard peer cleared
    }

    [Fact]
    public void AssignsDirections()
    {
        var parsed = SshOutputParser.Parse(Output, Ts);
        Assert.Equal(ConnectionDirection.Listen, parsed.Connections[0].Direction);
        Assert.Equal(ConnectionDirection.Inbound, parsed.Connections[1].Direction);    // to own port 80
        Assert.Equal(ConnectionDirection.Outbound, parsed.Connections[2].Direction);   // to 5432
        Assert.Equal(ConnectionDirection.Listen, parsed.Connections[3].Direction);     // UDP
    }

    [Fact]
    public void ParsesServicesWithStartMode()
    {
        var parsed = SshOutputParser.Parse(Output, Ts);
        Assert.Equal(2, parsed.Services.Count);

        var nginx = parsed.Services.Single(s => s.Name == "nginx");
        Assert.Equal("running", nginx.State);
        Assert.Equal("enabled", nginx.StartMode);
        Assert.Equal("Nginx HTTP server", nginx.DisplayName);

        var pg = parsed.Services.Single(s => s.Name == "postgresql");
        Assert.Equal("dead", pg.State);
        Assert.Equal("disabled", pg.StartMode);
    }

    private const string OutputWithConntrack = Output + """

        ###CT
        tcp      6 117 TIME_WAIT src=10.0.0.5 dst=10.0.0.53 sport=44890 dport=53 src=10.0.0.53 dst=10.0.0.5 sport=53 dport=44890 [ASSURED] mark=0 use=1
        tcp      6 431999 ESTABLISHED src=10.0.0.99 dst=10.0.0.5 sport=51555 dport=80 src=10.0.0.5 dst=10.0.0.99 sport=80 dport=51555 [ASSURED] mark=0 use=1
        tcp      6 431999 ESTABLISHED src=10.0.0.9 dst=10.0.0.5 sport=52100 dport=80 src=10.0.0.5 dst=10.0.0.9 sport=80 dport=52100 [ASSURED] mark=0 use=1
        udp      17 25 src=10.0.0.5 dst=10.0.0.53 sport=40000 dport=123 src=10.0.0.53 dst=10.0.0.5 sport=123 dport=40000 mark=0 use=1
        tcp      6 60 SYN_SENT src=172.30.1.1 dst=172.30.1.2 sport=1234 dport=80 src=172.30.1.2 dst=172.30.1.1 sport=80 dport=1234 mark=0 use=1
        """;

    [Fact]
    public void ConntrackAddsRecentlyClosedOutboundFlow()
    {
        var parsed = SshOutputParser.Parse(OutputWithConntrack, Ts);
        var dns = parsed.Connections.Single(c => c.RemotePort == 53 && c.Protocol == Protocol.Tcp);
        Assert.Equal(ConnectionDirection.Outbound, dns.Direction);
        Assert.Equal(TcpState.TimeWait, dns.State);
        Assert.Equal("10.0.0.5", dns.LocalAddress);
        Assert.Equal("10.0.0.53", dns.RemoteAddress);
    }

    [Fact]
    public void ConntrackClassifiesInboundFromOriginatorTuple()
    {
        var parsed = SshOutputParser.Parse(OutputWithConntrack, Ts);
        // 10.0.0.99 connected in to our port 80; not present in the ss section.
        var inbound = parsed.Connections.Single(c => c.RemoteAddress == "10.0.0.99");
        Assert.Equal(ConnectionDirection.Inbound, inbound.Direction);
        Assert.Equal(80, inbound.LocalPort);
    }

    [Fact]
    public void ConntrackSkipsFlowsAlreadySeenBySs()
    {
        var parsed = SshOutputParser.Parse(OutputWithConntrack, Ts);
        // The 10.0.0.9:52100 -> :80 flow appears in both ss and conntrack.
        Assert.Single(parsed.Connections.Where(c =>
            c.RemoteAddress == "10.0.0.9" && c.RemotePort == 52100));
    }

    [Fact]
    public void ConntrackSkipsForeignFlows()
    {
        var parsed = SshOutputParser.Parse(OutputWithConntrack, Ts);
        // 172.30.x traffic involves neither side of this host.
        Assert.DoesNotContain(parsed.Connections, c =>
            c.LocalAddress.StartsWith("172.30.") || c.RemoteAddress.StartsWith("172.30."));
    }

    [Fact]
    public void ConntrackParsesUdpFlows()
    {
        var parsed = SshOutputParser.Parse(OutputWithConntrack, Ts);
        var ntp = parsed.Connections.Single(c => c.Protocol == Protocol.Udp && c.RemotePort == 123);
        Assert.Equal(ConnectionDirection.Outbound, ntp.Direction);
    }

    [Fact]
    public void EmptyOutputYieldsEmptyResult()
    {
        var parsed = SshOutputParser.Parse(string.Empty, Ts);
        Assert.Empty(parsed.Connections);
        Assert.Empty(parsed.Services);
        Assert.Equal(string.Empty, parsed.MachineName);
    }
}
