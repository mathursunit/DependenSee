using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;
using Xunit;

namespace ServiceMap.Tests;

/// <summary>
/// Exercises the SQLite store end-to-end on a temp file: schema creation,
/// write-time flow aggregation, unique-flow queries, and split retention.
/// </summary>
public sealed class SampleRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"servicemap-test-{Guid.NewGuid():N}.db");
    private readonly SampleRepository _repo;

    public SampleRepositoryTests()
    {
        _repo = new SampleRepository(_dbPath);
        _repo.Initialize();
    }

    public void Dispose()
    {
        _repo.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    private static ConnectionSample Outbound(DateTime ts, int ephemeralPort,
        string remote = "10.9.9.9", int remotePort = 443, string svc = "") => new()
    {
        Protocol = Protocol.Tcp,
        State = TcpState.Established,
        Direction = ConnectionDirection.Outbound,
        LocalAddress = "10.0.0.5",
        LocalPort = ephemeralPort,
        RemoteAddress = remote,
        RemotePort = remotePort,
        ProcessId = 100,
        ProcessName = "myapp",
        ServiceName = svc,
        Timestamp = ts
    };

    [Fact]
    public void RepeatedSamplesCollapseIntoOneFlow()
    {
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        // Same logical dependency observed on 3 sweeps with rotating ephemeral ports.
        _repo.InsertConnectionSamples(new[] { Outbound(t0, 50001) });
        _repo.InsertConnectionSamples(new[] { Outbound(t0.AddMinutes(1), 50002) });
        _repo.InsertConnectionSamples(new[] { Outbound(t0.AddMinutes(2), 50003) });

        var flows = _repo.QueryUniqueConnections(new ConnectionQuery());
        var f = Assert.Single(flows);
        Assert.Equal(3, f.SampleCount);
        Assert.Equal("10.9.9.9", f.RemoteAddress);
        Assert.Equal(443, f.RemotePort);
        Assert.Equal(ConnectionDirection.Outbound, f.Direction);
        // first/last seen span the observations
        Assert.Equal(t0, f.FirstSeen, TimeSpan.FromSeconds(1));
        Assert.Equal(t0.AddMinutes(2), f.LastSeen, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DistinctDependenciesStayDistinct()
    {
        var t = DateTime.UtcNow;
        _repo.InsertConnectionSamples(new[]
        {
            Outbound(t, 50001, remote: "10.9.9.9", remotePort: 443),
            Outbound(t, 50002, remote: "10.9.9.9", remotePort: 1433),
            Outbound(t, 50003, remote: "10.8.8.8", remotePort: 443)
        });

        Assert.Equal(3, _repo.QueryUniqueConnections(new ConnectionQuery()).Count);
    }

    [Fact]
    public void LateServiceAttributionUpgradesTheFlow()
    {
        var t = DateTime.UtcNow;
        // First sweep before the service scan resolved the owning service…
        _repo.InsertConnectionSamples(new[] { Outbound(t, 50001, svc: "") });
        // …later sweeps carry it.
        _repo.InsertConnectionSamples(new[] { Outbound(t.AddMinutes(1), 50002, svc: "My App Service") });

        var f = Assert.Single(_repo.QueryUniqueConnections(new ConnectionQuery()));
        Assert.Equal("My App Service", f.ServiceName);
    }

    [Fact]
    public void FlowsSurviveRawSamplePruning()
    {
        var old = DateTime.UtcNow.AddDays(-10);
        _repo.InsertConnectionSamples(new[] { Outbound(old, 50001) });

        // Prune raw rows aggressively; keep flows for 30 days.
        _repo.PruneConnectionsOlderThan(
            rawCutoffUtc: DateTime.UtcNow.AddDays(-7),
            flowCutoffUtc: DateTime.UtcNow.AddDays(-30));

        Assert.Empty(_repo.QueryConnections(new ConnectionQuery()));          // raw gone
        Assert.Single(_repo.QueryUniqueConnections(new ConnectionQuery()));   // flow kept
    }

    [Fact]
    public void FlowPruneRemovesStaleDependencies()
    {
        var ancient = DateTime.UtcNow.AddDays(-40);
        _repo.InsertConnectionSamples(new[] { Outbound(ancient, 50001) });

        _repo.PruneConnectionsOlderThan(
            rawCutoffUtc: DateTime.UtcNow.AddDays(-7),
            flowCutoffUtc: DateTime.UtcNow.AddDays(-30));

        Assert.Empty(_repo.QueryUniqueConnections(new ConnectionQuery()));
    }

    [Fact]
    public void FlowQueryHonorsFilters()
    {
        var t = DateTime.UtcNow;
        _repo.InsertConnectionSamples(new[]
        {
            Outbound(t, 50001, remote: "10.9.9.9", remotePort: 443),
            Outbound(t, 50002, remote: "8.8.8.8", remotePort: 53)
        });

        var byRemote = _repo.QueryUniqueConnections(new ConnectionQuery { RemoteAddress = "10.9" });
        Assert.Single(byRemote);
        Assert.Equal("10.9.9.9", byRemote[0].RemoteAddress);

        var byScope = _repo.QueryUniqueConnections(new ConnectionQuery { Scope = IpScope.Public });
        Assert.Single(byScope);
        Assert.Equal("8.8.8.8", byScope[0].RemoteAddress);

        var byDir = _repo.QueryUniqueConnections(new ConnectionQuery { Direction = ConnectionDirection.Inbound });
        Assert.Empty(byDir);
    }

    [Fact]
    public void ListenersKeyOnLocalEndpoint()
    {
        var t = DateTime.UtcNow;
        var listener = new ConnectionSample
        {
            Protocol = Protocol.Tcp,
            State = TcpState.Listen,
            Direction = ConnectionDirection.Listen,
            LocalAddress = "0.0.0.0",
            LocalPort = 8080,
            ProcessId = 200,
            ProcessName = "websvc",
            Timestamp = t
        };
        _repo.InsertConnectionSamples(new[] { listener });
        _repo.InsertConnectionSamples(new[] { listener });

        var f = Assert.Single(_repo.QueryUniqueConnections(new ConnectionQuery()));
        Assert.Equal(2, f.SampleCount);
        Assert.Equal("0.0.0.0", f.LocalAddress);
        Assert.Equal(8080, f.LocalPort);
    }

    [Fact]
    public void SweepCountTracksDistinctBatchTimestamps()
    {
        var t = DateTime.UtcNow;
        // One batch containing two sweeps (burst), then a second batch of one.
        _repo.InsertConnectionSamples(new[] { Outbound(t, 50001), Outbound(t.AddSeconds(10), 50002) });
        _repo.InsertConnectionSamples(new[] { Outbound(t.AddMinutes(5), 50003) });

        Assert.Equal("3", _repo.GetMeta("sweep_count"));
    }

    [Fact]
    public void ExplicitSweepCountOverridesTimestampHeuristic()
    {
        var t = DateTime.UtcNow;
        // A local-collector batch: one poll sweep plus ETW events with their own
        // timestamps must still count as ONE sweep.
        _repo.InsertConnectionSamples(new[]
        {
            Outbound(t, 50001),
            Outbound(t.AddSeconds(-2), 50002),   // ETW event timestamp
            Outbound(t.AddSeconds(-4), 50003)    // ETW event timestamp
        }, sweepCount: 1);

        Assert.Equal("1", _repo.GetMeta("sweep_count"));
    }

    [Fact]
    public void TimeWindowUsesOverlapSemantics()
    {
        var t0 = DateTime.UtcNow.AddDays(-5);
        _repo.InsertConnectionSamples(new[] { Outbound(t0, 50001) });
        _repo.InsertConnectionSamples(new[] { Outbound(DateTime.UtcNow, 50002) });

        // Flow spans 5 days; a query on the last day must still see it.
        var flows = _repo.QueryUniqueConnections(new ConnectionQuery
        {
            From = DateTime.UtcNow.AddDays(-1)
        });
        Assert.Single(flows);
    }
}
