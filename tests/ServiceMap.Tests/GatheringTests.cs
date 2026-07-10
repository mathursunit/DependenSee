using ServiceMap.Core.Analysis;
using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;
using Xunit;

namespace ServiceMap.Tests;

public class ConfigScavengerTests
{
    [Fact]
    public void ExtractsConnectionStringHostAndMasksPassword()
    {
        var text = "<add name=\"db\" connectionString=\"Server=sql01.corp,1433;Database=fin;User Id=svc;Password=Sup3rSecret!\" />";
        var eps = ConfigScavenger.Scan("web.config", text);
        var cs = eps.Single(e => e.Kind == "connection-string");
        Assert.Equal("sql01.corp", cs.Host);
        Assert.Equal(1433, cs.Port);
        Assert.Contains("Password=***", cs.Redacted);
        Assert.DoesNotContain("Sup3rSecret", cs.Redacted);
    }

    [Fact]
    public void ExtractsUrlsWithDefaultPorts()
    {
        var eps = ConfigScavenger.Scan("appsettings.json",
            "{ \"Api\": \"https://api.internal.corp/v1\", \"Mq\": \"amqp://rabbit.corp:5672\" }");
        Assert.Contains(eps, e => e.Host == "api.internal.corp" && e.Port == 443);
        Assert.Contains(eps, e => e.Host == "rabbit.corp" && e.Port == 5672);
    }

    [Fact]
    public void IgnoresLocalhostAndSchemaUrls()
    {
        var eps = ConfigScavenger.Scan("app.config",
            "http://localhost:8080 http://www.w3.org/2001/XMLSchema http://schemas.microsoft.com/x");
        Assert.DoesNotContain(eps, e => e.Host is "localhost" or "www.w3.org");
    }

    [Fact]
    public void KeepRawStillMasksSecrets()
    {
        var text = "redis://cache01:6379,password=abc123,ssl=True";
        var raw = ConfigScavenger.Scan("x.env", text, keepRaw: true).First();
        Assert.DoesNotContain("abc123", raw.Redacted);
    }
}

public class IdentityMapTests
{
    private static ConnectionAggregate Out(int port, string remote = "10.50.1.10", long count = 10) => new()
    {
        Direction = ConnectionDirection.Outbound, Protocol = Protocol.Tcp,
        RemotePort = port, RemoteAddress = remote, SampleCount = count
    };

    [Fact]
    public void ClassifiesKerberosAndLdap()
    {
        var deps = IdentityMap.FromFlows(new[] { Out(88), Out(389), Out(443) });
        Assert.Contains(deps, d => d.Kind == "Kerberos");
        Assert.Contains(deps, d => d.Kind == "LDAP");
        Assert.DoesNotContain(deps, d => d.Port == 443);   // not an identity port
    }

    [Fact]
    public void NonBuiltinAccountsAreFlagged()
    {
        var services = new List<ServiceRecord>
        {
            new() { Name = "a", Account = "LocalSystem" },
            new() { Name = "b", Account = "NT AUTHORITY\\NetworkService" },
            new() { Name = "c", Account = "CORP\\svc-sql" },
            new() { Name = "d", Account = "NT SERVICE\\MSSQL" }
        };
        var flagged = IdentityMap.NonBuiltinServiceAccounts(services);
        Assert.Single(flagged);
        Assert.Equal("CORP\\svc-sql", flagged[0].Account);
    }
}

public class MetricRollupTests
{
    [Fact]
    public void PercentileInterpolates()
    {
        var vals = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        Assert.Equal(95.05, MetricRollup.Percentile(vals, 0.95), 2);
    }

    [Fact]
    public void SummarizesPeakAndP95()
    {
        var t = DateTime.UtcNow;
        var samples = new List<MetricSample>
        {
            new(t, 10, 1000, 5, 1),
            new(t.AddSeconds(30), 90, 2000, 50, 8),
            new(t.AddSeconds(60), 20, 1500, 10, 2),
        };
        var s = MetricRollup.Summarize(samples);
        Assert.Equal(3, s.SampleCount);
        Assert.Equal(90, s.CpuPeak);
        Assert.Equal(2000, s.MemPeakMb);
        Assert.True(s.CpuP95 >= s.CpuAvg);
    }

    [Fact]
    public void EmptyIsZero() => Assert.Equal(0, MetricRollup.Summarize(Array.Empty<MetricSample>()).SampleCount);
}

public class BaselineComparerTests
{
    private static ConnectionAggregate F(ConnectionDirection dir, int port, string remote) => new()
    {
        Direction = dir, Protocol = Protocol.Tcp,
        RemotePort = dir == ConnectionDirection.Outbound ? port : 51000,
        LocalPort = dir == ConnectionDirection.Outbound ? 51000 : port,
        RemoteAddress = remote, ProcessName = "app"
    };

    [Fact]
    public void DetectsMissingNewUnchanged()
    {
        var baseline = new[] { F(ConnectionDirection.Outbound, 443, "10.1.1.1"), F(ConnectionDirection.Outbound, 1433, "10.2.2.2") };
        var current = new[] { F(ConnectionDirection.Outbound, 443, "10.1.1.1"), F(ConnectionDirection.Outbound, 5432, "10.3.3.3") };
        var diff = BaselineComparer.Compare(baseline, current);

        Assert.Contains(diff, r => r.Change == FlowChange.Missing && r.Port == 1433);
        Assert.Contains(diff, r => r.Change == FlowChange.New && r.Port == 5432);
        Assert.Contains(diff, r => r.Change == FlowChange.Unchanged && r.Port == 443);
    }
}

public sealed class DnsMetricStorageTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cds-b-{Guid.NewGuid():N}.db");
    private readonly SampleRepository _repo;

    public DnsMetricStorageTests()
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

    [Fact]
    public void DnsUpsertFoldsCounts()
    {
        var now = DateTime.UtcNow;
        _repo.UpsertDnsResolutions(new[]
        {
            new DnsResolution { ProcessName = "app", QueryName = "api.corp", ResolvedAddress = "10.1.1.1", Count = 1, LastSeen = now },
        });
        _repo.UpsertDnsResolutions(new[]
        {
            new DnsResolution { ProcessName = "app", QueryName = "api.corp", ResolvedAddress = "10.1.1.1", Count = 4, LastSeen = now.AddMinutes(1) },
        });
        var rows = _repo.GetDnsResolutions();
        var r = Assert.Single(rows);
        Assert.Equal(5, r.Count);
        Assert.Equal("api.corp", r.QueryName);
    }

    [Fact]
    public void MetricSamplesRoundTrip()
    {
        var t = DateTime.UtcNow;
        _repo.InsertMetricSamples(new[] { (t, 42.0, 1024.0, 12.0, 3.0), (t.AddSeconds(30), 55.0, 2048.0, 20.0, 5.0) });
        var got = _repo.GetMetricSamples();
        Assert.Equal(2, got.Count);
        Assert.Equal(42.0, got[0].Cpu, 1);
    }

    [Fact]
    public void SchemaIsV5()
    {
        Assert.Equal("5", _repo.GetMeta("schema_version"));
    }
}
