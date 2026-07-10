using ServiceMap.Core.Analysis;
using ServiceMap.Core.Models;
using Xunit;

namespace ServiceMap.Tests;

public class ReadinessTests
{
    [Fact]
    public void NoFlowsIsInsufficient()
    {
        var r = Readiness.Compute(new ReadinessInput { FlowCount = 0 });
        Assert.Equal(0, r.Score);
        Assert.Equal("Insufficient", r.Rating);
    }

    [Fact]
    public void FullMarksWhenTargetsMet()
    {
        var r = Readiness.Compute(new ReadinessInput
        {
            FlowCount = 100, WindowDays = 20, SweepCount = 5000, AttributionPct = 1.0
        });
        Assert.Equal(100, r.Score);
        Assert.Equal("Ready", r.Rating);
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void ShortWindowIsExplained()
    {
        var r = Readiness.Compute(new ReadinessInput
        {
            FlowCount = 100, WindowDays = 2, SweepCount = 5000, AttributionPct = 1.0
        });
        Assert.True(r.Score < 80);
        Assert.Contains(r.Notes, n => n.Contains("2.0 days"));
    }

    [Fact]
    public void RemoteScanSourceIsNoted()
    {
        var r = Readiness.Compute(new ReadinessInput
        {
            FlowCount = 10, WindowDays = 20, SweepCount = 5000, AttributionPct = 1.0,
            CollectionSource = "remote-scan"
        });
        Assert.Contains(r.Notes, n => n.Contains("agentless"));
    }

    [Fact]
    public void FromFlowsComputesWindowAndAttribution()
    {
        var t = DateTime.UtcNow;
        var flows = new List<ConnectionAggregate>
        {
            new() { FirstSeen = t.AddDays(-7), LastSeen = t, ProcessName = "svc" },
            new() { FirstSeen = t.AddDays(-3), LastSeen = t, ProcessName = "" }
        };
        var input = Readiness.FromFlows(flows, 100, "local-collector");
        Assert.Equal(7, input.WindowDays, 1);
        Assert.Equal(0.5, input.AttributionPct, 2);
    }
}

public class RiskFlagsTests
{
    private static ConnectionAggregate Flow(ConnectionDirection dir, Protocol proto,
        int lport, int rport, string remote = "10.1.1.1",
        IpScope scope = IpScope.Private, long count = 5) => new()
    {
        Direction = dir, Protocol = proto, LocalPort = lport, RemotePort = rport,
        RemoteAddress = remote, RemoteScope = scope, SampleCount = count,
        FirstSeen = DateTime.UtcNow.AddDays(-10), LastSeen = DateTime.UtcNow
    };

    [Fact]
    public void FlagsTelnetServer()
    {
        var findings = RiskFlags.Analyze(new[]
        {
            Flow(ConnectionDirection.Inbound, Protocol.Tcp, 23, 51000)
        });
        Assert.Contains(findings, f => f.Severity == "High" && f.Title.Contains("Telnet"));
    }

    [Fact]
    public void FlagsOutboundFtp()
    {
        var findings = RiskFlags.Analyze(new[]
        {
            Flow(ConnectionDirection.Outbound, Protocol.Tcp, 51000, 21)
        });
        Assert.Contains(findings, f => f.Title.Contains("Outbound FTP"));
    }

    [Fact]
    public void FlagsInternetExposure()
    {
        var findings = RiskFlags.Analyze(new[]
        {
            Flow(ConnectionDirection.Inbound, Protocol.Tcp, 443, 51000, "203.0.113.9", IpScope.Public)
        });
        Assert.Contains(findings, f => f.Title.Contains("Internet-facing service on port 443"));
    }

    [Fact]
    public void CleanTrafficYieldsNoFindings()
    {
        var findings = RiskFlags.Analyze(new[]
        {
            Flow(ConnectionDirection.Outbound, Protocol.Tcp, 51000, 443),
            Flow(ConnectionDirection.Inbound, Protocol.Tcp, 8443, 51000)
        });
        Assert.Empty(findings);
    }

    [Fact]
    public void RecentlyAppearedFiltersByFirstSeen()
    {
        var old = Flow(ConnectionDirection.Outbound, Protocol.Tcp, 51000, 443);
        old.FirstSeen = DateTime.UtcNow.AddDays(-30);
        var fresh = Flow(ConnectionDirection.Outbound, Protocol.Tcp, 51000, 1433);
        fresh.FirstSeen = DateTime.UtcNow.AddDays(-2);
        var listener = Flow(ConnectionDirection.Listen, Protocol.Tcp, 80, 0);
        listener.FirstSeen = DateTime.UtcNow.AddDays(-1);   // listeners excluded

        var recent = RiskFlags.RecentlyAppeared(new[] { old, fresh, listener }, days: 7);
        Assert.Single(recent);
        Assert.Equal(1433, recent[0].RemotePort);
    }
}
