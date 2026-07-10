using ServiceMap.Core.Models;
using ServiceMap.Core.Storage;
using ServiceMap.Reporting;

namespace ServiceMap.App.Services;

/// <summary>
/// Read-only access to the collector's SQLite database for the GUI. Opens the
/// database in read-only mode per call; WAL mode lets this run concurrently with
/// the collector service writing.
/// </summary>
public sealed class DataAccess
{
    private readonly Func<string> _databasePath;

    public DataAccess(Func<string> databasePath) => _databasePath = databasePath;

    public bool DatabaseExists => File.Exists(_databasePath());

    public IReadOnlyList<ServiceRecord> GetLatestServices() =>
        WithRepo(r => r.GetLatestServices(), Array.Empty<ServiceRecord>());

    public IReadOnlyList<ConnectionSample> GetLatestConnections() =>
        WithRepo(r => r.GetLatestConnections(), Array.Empty<ConnectionSample>());

    public IReadOnlyList<ConnectionSample> Query(ConnectionQuery q) =>
        WithRepo(r =>
        {
            var machine = r.MachineName ?? string.Empty;
            var rows = r.QueryConnections(q);
            foreach (var row in rows) if (string.IsNullOrEmpty(row.Machine)) row.Machine = machine;
            return rows;
        }, Array.Empty<ConnectionSample>());

    public IReadOnlyList<ConnectionAggregate> QueryUnique(ConnectionQuery q) =>
        WithRepo(r =>
        {
            var machine = r.MachineName ?? string.Empty;
            var rows = r.QueryUniqueConnections(q);
            foreach (var row in rows) if (string.IsNullOrEmpty(row.Machine)) row.Machine = machine;
            return rows;
        }, Array.Empty<ConnectionAggregate>());

    /// <summary>The machine's own /24 subnet prefixes (e.g. "10.0.0"), for the same-subnet filter.</summary>
    public IReadOnlyCollection<string> GetLocalSubnets() =>
        WithRepo(r =>
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ip in r.GetLocalAddresses())
            {
                var parts = ip.Split('.');
                if (parts.Length == 4) set.Add($"{parts[0]}.{parts[1]}.{parts[2]}");
            }
            return (IReadOnlyCollection<string>)set;
        }, Array.Empty<string>());

    /// <summary>The machine's own IP addresses (as recorded in local_address).</summary>
    public IReadOnlyList<string> GetLocalAddresses() =>
        WithRepo(r => r.GetLocalAddresses(), (IReadOnlyList<string>)Array.Empty<string>());

    public RepositoryStats GetStats() =>
        WithRepo(r => r.GetStats(), new RepositoryStats());

    public string? GetMachineName() =>
        WithRepo(r => r.MachineName, null);

    /// <summary>Read a meta key (e.g. sweep_count, collection_source) from the database.</summary>
    public string? GetMeta(string key) =>
        WithRepo(r => r.GetMeta(key), null);

    /// <summary>Build the firewall-rule report from stored data.</summary>
    public FirewallReport BuildFirewallReport(ConnectionQuery q, FirewallReportOptions o) =>
        WithRepo(r => FirewallReportBuilder.Build(r, q, o), new FirewallReport());

    private T WithRepo<T>(Func<SampleRepository, T> action, T fallback)
    {
        if (!DatabaseExists) return fallback;
        try
        {
            using var repo = new SampleRepository(_databasePath(), readOnly: true);
            return action(repo);
        }
        catch
        {
            return fallback;
        }
    }
}
