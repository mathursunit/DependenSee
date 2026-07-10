using System.Text;
using Microsoft.Data.Sqlite;
using ServiceMap.Core.Models;
using ServiceMap.Core.Net;

namespace ServiceMap.Core.Storage;

/// <summary>
/// SQLite-backed store for service snapshots and connection samples.
/// WAL journaling is enabled so the collector (writer) and GUI (reader) can
/// access the same file concurrently.
/// </summary>
public sealed class SampleRepository : IDisposable
{
    private const int SchemaVersion = 5;

    private readonly string _connectionString;
    private readonly bool _readOnly;

    public SampleRepository(string databasePath, bool readOnly = false)
    {
        _readOnly = readOnly;

        if (!readOnly)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };
        _connectionString = builder.ToString();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = _readOnly
            ? "PRAGMA busy_timeout=5000;"
            : "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    /// <summary>Create tables/indexes and apply migrations if needed. Writer only.</summary>
    public void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS connection_samples (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                protocol       INTEGER NOT NULL,
                local_address  TEXT NOT NULL,
                local_port     INTEGER NOT NULL,
                remote_address TEXT NOT NULL,
                remote_port    INTEGER NOT NULL,
                state          INTEGER NOT NULL,
                direction      INTEGER NOT NULL,
                remote_scope   INTEGER NOT NULL DEFAULT 0,
                process_id     INTEGER NOT NULL,
                process_name   TEXT NOT NULL,
                process_path   TEXT,
                service_name   TEXT,
                timestamp      TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_conn_timestamp ON connection_samples(timestamp);
            CREATE INDEX IF NOT EXISTS ix_conn_process   ON connection_samples(process_name);
            CREATE INDEX IF NOT EXISTS ix_conn_remote    ON connection_samples(remote_address);
            CREATE INDEX IF NOT EXISTS ix_conn_lport     ON connection_samples(local_port);
            CREATE INDEX IF NOT EXISTS ix_conn_scope     ON connection_samples(remote_scope);

            CREATE TABLE IF NOT EXISTS service_snapshots (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                name            TEXT NOT NULL,
                display_name    TEXT NOT NULL,
                state           TEXT NOT NULL,
                start_mode      TEXT NOT NULL,
                process_id      INTEGER NOT NULL,
                executable_path TEXT,
                account         TEXT,
                scan_timestamp  TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_svc_scan ON service_snapshots(scan_timestamp);
            CREATE INDEX IF NOT EXISTS ix_svc_name ON service_snapshots(name);

            -- v4: write-time aggregated flows. One row per distinct dependency
            -- (direction-aware key, ephemeral ports collapsed), upserted on every
            -- sweep. Raw connection_samples stay for short-horizon drill-down;
            -- flows carry the migration-relevant view for the full retention
            -- window at a tiny fraction of the size.
            CREATE TABLE IF NOT EXISTS connection_flows (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                protocol       INTEGER NOT NULL,
                direction      INTEGER NOT NULL,
                remote_scope   INTEGER NOT NULL,
                process_name   TEXT NOT NULL,
                local_address  TEXT NOT NULL,   -- key form: only for listeners
                local_port     INTEGER NOT NULL,-- key form: 0 for outbound
                remote_address TEXT NOT NULL,   -- key form: '' for listeners
                remote_port    INTEGER NOT NULL,-- key form: 0 unless outbound
                service_name   TEXT NOT NULL DEFAULT '',
                owner_address  TEXT NOT NULL DEFAULT '',
                first_seen     TEXT NOT NULL,
                last_seen      TEXT NOT NULL,
                sample_count   INTEGER NOT NULL,
                UNIQUE(protocol, direction, remote_scope, process_name,
                       local_address, local_port, remote_address, remote_port)
            );

            CREATE INDEX IF NOT EXISTS ix_flow_last    ON connection_flows(last_seen);
            CREATE INDEX IF NOT EXISTS ix_flow_process ON connection_flows(process_name);
            CREATE INDEX IF NOT EXISTS ix_flow_remote  ON connection_flows(remote_address);

            -- v5: distinct DNS resolutions (name<->IP per process), folded like flows.
            CREATE TABLE IF NOT EXISTS dns_resolutions (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                process_name   TEXT NOT NULL,
                query_name     TEXT NOT NULL,
                resolved_addr  TEXT NOT NULL,
                first_seen     TEXT NOT NULL,
                last_seen      TEXT NOT NULL,
                sample_count   INTEGER NOT NULL,
                UNIQUE(process_name, query_name, resolved_addr)
            );
            CREATE INDEX IF NOT EXISTS ix_dns_last ON dns_resolutions(last_seen);
            CREATE INDEX IF NOT EXISTS ix_dns_name ON dns_resolutions(query_name);

            -- v5: resource-utilization samples for right-sizing.
            CREATE TABLE IF NOT EXISTS metric_samples (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp    TEXT NOT NULL,
                cpu_pct      REAL NOT NULL,
                mem_used_mb  REAL NOT NULL,
                disk_iops    REAL NOT NULL,
                net_mbps     REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_metric_ts ON metric_samples(timestamp);
            """;
        cmd.ExecuteNonQuery();

        // Migration: add remote_scope to a pre-v2 connection_samples table.
        if (!ColumnExists(conn, "connection_samples", "remote_scope"))
        {
            using var alter = conn.CreateCommand();
            alter.CommandText =
                "ALTER TABLE connection_samples ADD COLUMN remote_scope INTEGER NOT NULL DEFAULT 0;";
            alter.ExecuteNonQuery();
        }
        if (!ColumnExists(conn, "connection_samples", "service_name"))
        {
            using var alter2 = conn.CreateCommand();
            alter2.CommandText = "ALTER TABLE connection_samples ADD COLUMN service_name TEXT;";
            alter2.ExecuteNonQuery();
        }

        // Migration to v4: backfill connection_flows from existing raw samples so
        // the flow table is authoritative from the moment it appears (otherwise
        // history collected before the upgrade would vanish from unique views).
        if (GetSchemaVersion(conn) < 4)
        {
            using var backfill = conn.CreateCommand();
            backfill.CommandText = """
                INSERT INTO connection_flows
                    (protocol, direction, remote_scope, process_name,
                     local_address, local_port, remote_address, remote_port,
                     service_name, owner_address, first_seen, last_seen, sample_count)
                SELECT protocol, direction, remote_scope, process_name,
                       CASE WHEN direction=0 THEN local_address ELSE '' END,
                       CASE WHEN direction IN (0,1) THEN local_port ELSE 0 END,
                       CASE WHEN direction=0 THEN '' ELSE remote_address END,
                       CASE WHEN direction=2 THEN remote_port ELSE 0 END,
                       COALESCE(MAX(service_name), ''), MAX(local_address),
                       MIN(timestamp), MAX(timestamp), COUNT(*)
                FROM connection_samples
                GROUP BY 1,2,3,4,5,6,7,8
                ON CONFLICT(protocol, direction, remote_scope, process_name,
                            local_address, local_port, remote_address, remote_port)
                DO UPDATE SET
                    first_seen   = MIN(first_seen, excluded.first_seen),
                    last_seen    = MAX(last_seen, excluded.last_seen),
                    sample_count = sample_count + excluded.sample_count;
                """;
            backfill.ExecuteNonQuery();
        }

        using var setVer = conn.CreateCommand();
        setVer.CommandText = "INSERT INTO meta(key,value) VALUES('schema_version',$v) " +
                             "ON CONFLICT(key) DO UPDATE SET value=$v;";
        setVer.Parameters.AddWithValue("$v", SchemaVersion.ToString());
        setVer.ExecuteNonQuery();
    }

    private static int GetSchemaVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key='schema_version';";
        return int.TryParse(cmd.ExecuteScalar() as string, out var v) ? v : 0;
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <param name="samples">Rows to store.</param>
    /// <param name="sweepCount">
    /// How many sampling sweeps this batch represents. When null it is
    /// estimated as the number of distinct timestamps - correct for pure
    /// polling batches, but event-captured samples carry their own timestamps,
    /// so callers that mix them in (the local collection engine) or that know
    /// the true count (remote bursts) should pass it explicitly.
    /// </param>
    public void InsertConnectionSamples(IReadOnlyCollection<ConnectionSample> samples, int? sweepCount = null)
    {
        if (samples.Count == 0) return;

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO connection_samples
                (protocol, local_address, local_port, remote_address, remote_port,
                 state, direction, remote_scope, process_id, process_name, process_path, service_name, timestamp)
            VALUES ($proto,$laddr,$lport,$raddr,$rport,$state,$dir,$scope,$pid,$pname,$ppath,$svc,$ts);
            """;
        var pProto = cmd.Parameters.Add("$proto", SqliteType.Integer);
        var pLaddr = cmd.Parameters.Add("$laddr", SqliteType.Text);
        var pLport = cmd.Parameters.Add("$lport", SqliteType.Integer);
        var pRaddr = cmd.Parameters.Add("$raddr", SqliteType.Text);
        var pRport = cmd.Parameters.Add("$rport", SqliteType.Integer);
        var pState = cmd.Parameters.Add("$state", SqliteType.Integer);
        var pDir = cmd.Parameters.Add("$dir", SqliteType.Integer);
        var pScope = cmd.Parameters.Add("$scope", SqliteType.Integer);
        var pPid = cmd.Parameters.Add("$pid", SqliteType.Integer);
        var pName = cmd.Parameters.Add("$pname", SqliteType.Text);
        var pPath = cmd.Parameters.Add("$ppath", SqliteType.Text);
        var pSvc = cmd.Parameters.Add("$svc", SqliteType.Text);
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);

        foreach (var s in samples)
        {
            var scope = s.RemoteScope != IpScope.None
                ? s.RemoteScope
                : IpClassifier.Classify(s.RemoteAddress);

            pProto.Value = (int)s.Protocol;
            pLaddr.Value = s.LocalAddress;
            pLport.Value = s.LocalPort;
            pRaddr.Value = s.RemoteAddress;
            pRport.Value = s.RemotePort;
            pState.Value = (int)s.State;
            pDir.Value = (int)s.Direction;
            pScope.Value = (int)scope;
            pPid.Value = s.ProcessId;
            pName.Value = s.ProcessName;
            pPath.Value = (object?)s.ProcessPath ?? DBNull.Value;
            pSvc.Value = string.IsNullOrEmpty(s.ServiceName) ? (object)DBNull.Value : s.ServiceName;
            pTs.Value = ToIso(s.Timestamp);
            cmd.ExecuteNonQuery();
        }

        UpsertFlows(conn, tx, samples);
        IncrementSweepCount(conn, tx,
            sweepCount ?? samples.Select(s => s.Timestamp).Distinct().Count());
        tx.Commit();
    }

    /// <summary>
    /// Track how many sampling sweeps this database contains, so reports can
    /// state their observation density. Survives raw-sample pruning because it
    /// lives in meta.
    /// </summary>
    private static void IncrementSweepCount(SqliteConnection conn, SqliteTransaction tx, int sweeps)
    {
        if (sweeps <= 0) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO meta(key,value) VALUES('sweep_count',$n) " +
                          "ON CONFLICT(key) DO UPDATE SET value = CAST(value AS INTEGER) + $n;";
        cmd.Parameters.AddWithValue("$n", sweeps);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Fold the sweep into the aggregated flow table (same transaction as the
    /// raw insert). One upsert per sample; the direction-aware key collapses
    /// ephemeral ports exactly like the read-side unique query used to.
    /// </summary>
    private static void UpsertFlows(SqliteConnection conn, SqliteTransaction tx,
        IReadOnlyCollection<ConnectionSample> samples)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO connection_flows
                (protocol, direction, remote_scope, process_name,
                 local_address, local_port, remote_address, remote_port,
                 service_name, owner_address, first_seen, last_seen, sample_count)
            VALUES ($proto,$dir,$scope,$pname,$laddr,$lport,$raddr,$rport,$svc,$owner,$ts,$ts,1)
            ON CONFLICT(protocol, direction, remote_scope, process_name,
                        local_address, local_port, remote_address, remote_port)
            DO UPDATE SET
                first_seen   = MIN(first_seen, excluded.first_seen),
                last_seen    = MAX(last_seen, excluded.last_seen),
                sample_count = sample_count + 1,
                service_name = CASE WHEN excluded.service_name <> ''
                                    THEN excluded.service_name ELSE service_name END,
                owner_address = CASE WHEN excluded.owner_address <> ''
                                     THEN excluded.owner_address ELSE owner_address END;
            """;
        var pProto = cmd.Parameters.Add("$proto", SqliteType.Integer);
        var pDir = cmd.Parameters.Add("$dir", SqliteType.Integer);
        var pScope = cmd.Parameters.Add("$scope", SqliteType.Integer);
        var pName = cmd.Parameters.Add("$pname", SqliteType.Text);
        var pLaddr = cmd.Parameters.Add("$laddr", SqliteType.Text);
        var pLport = cmd.Parameters.Add("$lport", SqliteType.Integer);
        var pRaddr = cmd.Parameters.Add("$raddr", SqliteType.Text);
        var pRport = cmd.Parameters.Add("$rport", SqliteType.Integer);
        var pSvc = cmd.Parameters.Add("$svc", SqliteType.Text);
        var pOwner = cmd.Parameters.Add("$owner", SqliteType.Text);
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);

        foreach (var s in samples)
        {
            var scope = s.RemoteScope != IpScope.None
                ? s.RemoteScope
                : IpClassifier.Classify(s.RemoteAddress);
            var isListen = s.Direction == ConnectionDirection.Listen;
            var isInbound = s.Direction == ConnectionDirection.Inbound;
            var isOutbound = s.Direction == ConnectionDirection.Outbound;

            pProto.Value = (int)s.Protocol;
            pDir.Value = (int)s.Direction;
            pScope.Value = (int)scope;
            pName.Value = s.ProcessName;
            pLaddr.Value = isListen ? s.LocalAddress : string.Empty;
            pLport.Value = isListen || isInbound ? s.LocalPort : 0;
            pRaddr.Value = isListen ? string.Empty : s.RemoteAddress;
            pRport.Value = isOutbound ? s.RemotePort : 0;
            pSvc.Value = s.ServiceName ?? string.Empty;
            pOwner.Value = s.LocalAddress;
            pTs.Value = ToIso(s.Timestamp);
            cmd.ExecuteNonQuery();
        }
    }

    public void InsertServiceSnapshot(IReadOnlyCollection<ServiceRecord> services)
    {
        if (services.Count == 0) return;

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO service_snapshots
                (name, display_name, state, start_mode, process_id,
                 executable_path, account, scan_timestamp)
            VALUES ($name,$disp,$state,$mode,$pid,$path,$acct,$ts);
            """;
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pDisp = cmd.Parameters.Add("$disp", SqliteType.Text);
        var pState = cmd.Parameters.Add("$state", SqliteType.Text);
        var pMode = cmd.Parameters.Add("$mode", SqliteType.Text);
        var pPid = cmd.Parameters.Add("$pid", SqliteType.Integer);
        var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
        var pAcct = cmd.Parameters.Add("$acct", SqliteType.Text);
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);

        foreach (var s in services)
        {
            pName.Value = s.Name;
            pDisp.Value = s.DisplayName;
            pState.Value = s.State;
            pMode.Value = s.StartMode;
            pPid.Value = s.ProcessId;
            pPath.Value = (object?)s.ExecutablePath ?? DBNull.Value;
            pAcct.Value = (object?)s.Account ?? DBNull.Value;
            pTs.Value = ToIso(s.ScanTimestamp);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Connections from the most recent sampling sweep.</summary>
    public IReadOnlyList<ConnectionSample> GetLatestConnections()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT " + ConnColumns + " FROM connection_samples " +
            "WHERE timestamp = (SELECT MAX(timestamp) FROM connection_samples) " +
            "ORDER BY process_name, local_port;";
        return ReadConnections(cmd);
    }

    public IReadOnlyList<ServiceRecord> GetLatestServices()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name, display_name, state, start_mode, process_id,
                   executable_path, account, scan_timestamp
            FROM service_snapshots
            WHERE scan_timestamp = (SELECT MAX(scan_timestamp) FROM service_snapshots)
            ORDER BY display_name;
            """;
        var list = new List<ServiceRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ServiceRecord
            {
                Name = r.GetString(0),
                DisplayName = r.GetString(1),
                State = r.GetString(2),
                StartMode = r.GetString(3),
                ProcessId = r.GetInt32(4),
                ExecutablePath = r.IsDBNull(5) ? null : r.GetString(5),
                Account = r.IsDBNull(6) ? null : r.GetString(6),
                ScanTimestamp = FromIso(r.GetString(7))
            });
        }
        return list;
    }

    public IReadOnlyList<ConnectionSample> QueryConnections(ConnectionQuery q)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();

        var sb = new StringBuilder("SELECT " + ConnColumns + " FROM connection_samples WHERE 1=1");
        AppendFilters(sb, cmd, q);
        sb.Append(" ORDER BY timestamp DESC, id DESC LIMIT $limit");
        cmd.Parameters.AddWithValue("$limit", q.Limit <= 0 ? 5000 : q.Limit);

        cmd.CommandText = sb.ToString();
        return ReadConnections(cmd);
    }

    /// <summary>
    /// Distinct flows matching the query. Served from the write-time aggregated
    /// connection_flows table when present (v4+ databases — covers the full
    /// retention window and is far faster); falls back to aggregating raw
    /// samples for databases produced by older collectors. The grouping key is
    /// direction-aware so ephemeral ports don't create false uniques:
    /// listeners key on local address+port; inbound on local port + remote
    /// address; outbound on remote address+port.
    /// </summary>
    public IReadOnlyList<ConnectionAggregate> QueryUniqueConnections(ConnectionQuery q)
    {
        using var conn = Open();

        if (TableExists(conn, "connection_flows"))
            return QueryFlows(conn, q);

        using var cmd = conn.CreateCommand();

        // Direction: Listen=0, Inbound=1, Outbound=2.
        const string kLaddr = "CASE WHEN direction=0 THEN local_address ELSE '' END";
        const string kLport = "CASE WHEN direction IN (0,1) THEN local_port ELSE 0 END";
        const string kRaddr = "CASE WHEN direction=0 THEN '' ELSE remote_address END";
        const string kRport = "CASE WHEN direction=2 THEN remote_port ELSE 0 END";

        var sb = new StringBuilder();
        sb.Append("SELECT protocol, direction, remote_scope, process_name, ");
        sb.Append(kLaddr + " AS k_laddr, " + kLport + " AS k_lport, ");
        sb.Append(kRaddr + " AS k_raddr, " + kRport + " AS k_rport, ");
        sb.Append("MIN(timestamp) AS first_seen, MAX(timestamp) AS last_seen, COUNT(*) AS n, MAX(service_name) AS svc, MAX(local_address) AS owner_addr ");
        sb.Append("FROM connection_samples WHERE 1=1");
        AppendFilters(sb, cmd, q);
        sb.Append(" GROUP BY protocol, direction, remote_scope, process_name, ");
        sb.Append("k_laddr, k_lport, k_raddr, k_rport ");
        sb.Append("ORDER BY last_seen DESC LIMIT $limit");
        cmd.Parameters.AddWithValue("$limit", q.Limit <= 0 ? 5000 : q.Limit);

        cmd.CommandText = sb.ToString();

        var list = new List<ConnectionAggregate>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ConnectionAggregate
            {
                Protocol = (Protocol)r.GetInt32(0),
                Direction = (ConnectionDirection)r.GetInt32(1),
                RemoteScope = (IpScope)r.GetInt32(2),
                ProcessName = r.GetString(3),
                LocalAddress = r.GetString(4),
                LocalPort = r.GetInt32(5),
                RemoteAddress = r.GetString(6),
                RemotePort = r.GetInt32(7),
                FirstSeen = FromIso(r.GetString(8)),
                LastSeen = FromIso(r.GetString(9)),
                SampleCount = r.GetInt64(10),
                ServiceName = r.IsDBNull(11) ? string.Empty : r.GetString(11),
                OwnerAddress = r.IsDBNull(12) ? string.Empty : r.GetString(12)
            });
        }
        return list;
    }

    /// <summary>Read distinct flows from the write-time aggregate table.</summary>
    private static IReadOnlyList<ConnectionAggregate> QueryFlows(SqliteConnection conn, ConnectionQuery q)
    {
        using var cmd = conn.CreateCommand();

        // Raw local_address is '' on non-listener flow rows; fall back to the
        // owning machine address so address filters behave like the raw query.
        const string laddrExpr = "CASE WHEN local_address <> '' THEN local_address ELSE owner_address END";

        var sb = new StringBuilder(
            "SELECT protocol, direction, remote_scope, process_name, " +
            "local_address, local_port, remote_address, remote_port, " +
            "first_seen, last_seen, sample_count, service_name, owner_address " +
            "FROM connection_flows WHERE 1=1");

        // Time window: keep flows that overlap [From, To].
        if (q.From is { } from)
        {
            sb.Append(" AND last_seen >= $from");
            cmd.Parameters.AddWithValue("$from", ToIso(from));
        }
        if (q.To is { } to)
        {
            sb.Append(" AND first_seen <= $to");
            cmd.Parameters.AddWithValue("$to", ToIso(to));
        }
        if (!string.IsNullOrWhiteSpace(q.ProcessName))
        {
            sb.Append(" AND process_name LIKE $pname");
            cmd.Parameters.AddWithValue("$pname", "%" + q.ProcessName.Trim() + "%");
        }
        if (q.LocalPort is { } lport)
        {
            sb.Append(" AND local_port = $lport");
            cmd.Parameters.AddWithValue("$lport", lport);
        }
        if (q.LocalPortNot is { } lportNot)
        {
            sb.Append(" AND local_port <> $lportNot");
            cmd.Parameters.AddWithValue("$lportNot", lportNot);
        }
        if (!string.IsNullOrWhiteSpace(q.LocalAddress))
        {
            sb.Append(" AND " + laddrExpr + " LIKE $laddr");
            cmd.Parameters.AddWithValue("$laddr", "%" + q.LocalAddress.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(q.LocalNotContains))
        {
            sb.Append(" AND " + laddrExpr + " NOT LIKE $laddrNot");
            cmd.Parameters.AddWithValue("$laddrNot", "%" + q.LocalNotContains.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(q.RemoteAddress))
        {
            sb.Append(" AND remote_address LIKE $raddr");
            cmd.Parameters.AddWithValue("$raddr", "%" + q.RemoteAddress.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(q.RemoteNotContains))
        {
            sb.Append(" AND remote_address NOT LIKE $raddrNot");
            cmd.Parameters.AddWithValue("$raddrNot", "%" + q.RemoteNotContains.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(q.ProcessNotContains))
        {
            sb.Append(" AND COALESCE(NULLIF(service_name,''), process_name) NOT LIKE $pnameNot" +
                      " AND process_name NOT LIKE $pnameNot");
            cmd.Parameters.AddWithValue("$pnameNot", "%" + q.ProcessNotContains.Trim() + "%");
        }
        if (q.Protocol is { } proto)
        {
            sb.Append(" AND protocol = $proto");
            cmd.Parameters.AddWithValue("$proto", (int)proto);
        }
        if (q.Direction is { } dir)
        {
            sb.Append(" AND direction = $dir");
            cmd.Parameters.AddWithValue("$dir", (int)dir);
        }
        if (q.AddressFamily == AddressFamilyOption.IPv4)
            sb.Append(" AND " + laddrExpr + " NOT LIKE '%:%'");
        else if (q.AddressFamily == AddressFamilyOption.IPv6)
            sb.Append(" AND " + laddrExpr + " LIKE '%:%'");
        if (q.Scope is { } scope)
        {
            sb.Append(" AND remote_scope = $scope");
            cmd.Parameters.AddWithValue("$scope", (int)scope);
        }
        if (q.ExcludeEphemeral)
        {
            sb.Append(" AND NOT ((direction IN (0,1) AND local_port >= $eph)" +
                      " OR (direction = 2 AND remote_port >= $eph))");
            cmd.Parameters.AddWithValue("$eph", q.EphemeralThreshold);
        }

        sb.Append(" ORDER BY last_seen DESC LIMIT $limit");
        cmd.Parameters.AddWithValue("$limit", q.Limit <= 0 ? 5000 : q.Limit);
        cmd.CommandText = sb.ToString();

        var list = new List<ConnectionAggregate>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ConnectionAggregate
            {
                Protocol = (Protocol)r.GetInt32(0),
                Direction = (ConnectionDirection)r.GetInt32(1),
                RemoteScope = (IpScope)r.GetInt32(2),
                ProcessName = r.GetString(3),
                LocalAddress = r.GetString(4),
                LocalPort = r.GetInt32(5),
                RemoteAddress = r.GetString(6),
                RemotePort = r.GetInt32(7),
                FirstSeen = FromIso(r.GetString(8)),
                LastSeen = FromIso(r.GetString(9)),
                SampleCount = r.GetInt64(10),
                ServiceName = r.GetString(11),
                OwnerAddress = r.GetString(12)
            });
        }
        return list;
    }

    /// <summary>Shared WHERE-clause builder for both raw and unique queries.</summary>
    private static void AppendFilters(StringBuilder sb, SqliteCommand cmd, ConnectionQuery q)
    {
        if (q.From is { } from)
        {
            sb.Append(" AND timestamp >= $from");
            cmd.Parameters.AddWithValue("$from", ToIso(from));
        }
        if (q.To is { } to)
        {
            sb.Append(" AND timestamp <= $to");
            cmd.Parameters.AddWithValue("$to", ToIso(to));
        }
        if (!string.IsNullOrWhiteSpace(q.ProcessName))
        {
            sb.Append(" AND process_name LIKE $pname");
            cmd.Parameters.AddWithValue("$pname", "%" + q.ProcessName.Trim() + "%");
        }
        if (q.LocalPort is { } lport)
        {
            sb.Append(" AND local_port = $lport");
            cmd.Parameters.AddWithValue("$lport", lport);
        }
        if (q.LocalPortNot is { } lportNot)
        {
            sb.Append(" AND local_port <> $lportNot");
            cmd.Parameters.AddWithValue("$lportNot", lportNot);
        }
        if (!string.IsNullOrWhiteSpace(q.LocalAddress))
        {
            sb.Append(" AND local_address LIKE $laddr");
            cmd.Parameters.AddWithValue("$laddr", "%" + q.LocalAddress.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(q.LocalNotContains))
        {
            sb.Append(" AND local_address NOT LIKE $laddrNot");
            cmd.Parameters.AddWithValue("$laddrNot", "%" + q.LocalNotContains.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(q.RemoteAddress))
        {
            sb.Append(" AND remote_address LIKE $raddr");
            cmd.Parameters.AddWithValue("$raddr", "%" + q.RemoteAddress.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(q.ProcessNotContains))
        {
            sb.Append(" AND COALESCE(service_name, process_name) NOT LIKE $pnameNot AND process_name NOT LIKE $pnameNot");
            cmd.Parameters.AddWithValue("$pnameNot", "%" + q.ProcessNotContains.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(q.RemoteNotContains))
        {
            sb.Append(" AND remote_address NOT LIKE $raddrNot");
            cmd.Parameters.AddWithValue("$raddrNot", "%" + q.RemoteNotContains.Trim() + "%");
        }
        if (q.Protocol is { } proto)
        {
            sb.Append(" AND protocol = $proto");
            cmd.Parameters.AddWithValue("$proto", (int)proto);
        }
        if (q.Direction is { } dir)
        {
            sb.Append(" AND direction = $dir");
            cmd.Parameters.AddWithValue("$dir", (int)dir);
        }
        // Address family judged from the machine's local address: IPv6 literals
        // contain a colon; IPv4 does not.
        if (q.AddressFamily == AddressFamilyOption.IPv4)
            sb.Append(" AND local_address NOT LIKE '%:%'");
        else if (q.AddressFamily == AddressFamilyOption.IPv6)
            sb.Append(" AND local_address LIKE '%:%'");

        if (q.Scope is { } scope)
        {
            sb.Append(" AND remote_scope = $scope");
            cmd.Parameters.AddWithValue("$scope", (int)scope);
        }
        if (q.ExcludeEphemeral)
        {
            // Hide rows whose service-side port is in the dynamic/ephemeral range.
            sb.Append(" AND NOT ((direction IN (0,1) AND local_port >= $eph) OR (direction = 2 AND remote_port >= $eph))");
            cmd.Parameters.AddWithValue("$eph", q.EphemeralThreshold);
        }
    }

    /// <summary>
    /// Prune raw samples and service snapshots older than <paramref name="rawCutoffUtc"/>,
    /// and aggregated flows not seen since <paramref name="flowCutoffUtc"/>.
    /// Raw rows dominate database size, so they use the shorter horizon; flows
    /// are one row per distinct dependency and keep the long view.
    /// </summary>
    public int PruneConnectionsOlderThan(DateTime rawCutoffUtc, DateTime? flowCutoffUtc = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM connection_samples WHERE timestamp < $cut;";
        cmd.Parameters.AddWithValue("$cut", ToIso(rawCutoffUtc));
        var n = cmd.ExecuteNonQuery();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "DELETE FROM service_snapshots WHERE scan_timestamp < $cut;";
        cmd2.Parameters.AddWithValue("$cut", ToIso(rawCutoffUtc));
        cmd2.ExecuteNonQuery();

        if (TableExists(conn, "connection_flows"))
        {
            using var cmd3 = conn.CreateCommand();
            cmd3.CommandText = "DELETE FROM connection_flows WHERE last_seen < $cut;";
            cmd3.Parameters.AddWithValue("$cut", ToIso(flowCutoffUtc ?? rawCutoffUtc));
            cmd3.ExecuteNonQuery();
        }
        if (TableExists(conn, "dns_resolutions"))
        {
            using var cmd4 = conn.CreateCommand();
            cmd4.CommandText = "DELETE FROM dns_resolutions WHERE last_seen < $cut;";
            cmd4.Parameters.AddWithValue("$cut", ToIso(flowCutoffUtc ?? rawCutoffUtc));
            cmd4.ExecuteNonQuery();
        }
        if (TableExists(conn, "metric_samples"))
        {
            using var cmd5 = conn.CreateCommand();
            cmd5.CommandText = "DELETE FROM metric_samples WHERE timestamp < $cut;";
            cmd5.Parameters.AddWithValue("$cut", ToIso(rawCutoffUtc));
            cmd5.ExecuteNonQuery();
        }

        return n;
    }

    /// <summary>Upsert distinct DNS resolutions (process, name, IP), folding counts.</summary>
    public void UpsertDnsResolutions(IReadOnlyCollection<DnsResolution> rows)
    {
        if (rows.Count == 0) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO dns_resolutions
                (process_name, query_name, resolved_addr, first_seen, last_seen, sample_count)
            VALUES ($p,$n,$a,$ts,$ts,$c)
            ON CONFLICT(process_name, query_name, resolved_addr) DO UPDATE SET
                first_seen   = MIN(first_seen, excluded.first_seen),
                last_seen    = MAX(last_seen, excluded.last_seen),
                sample_count = sample_count + excluded.sample_count;
            """;
        var pP = cmd.Parameters.Add("$p", SqliteType.Text);
        var pN = cmd.Parameters.Add("$n", SqliteType.Text);
        var pA = cmd.Parameters.Add("$a", SqliteType.Text);
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);
        var pC = cmd.Parameters.Add("$c", SqliteType.Integer);
        foreach (var r in rows)
        {
            pP.Value = r.ProcessName ?? string.Empty;
            pN.Value = r.QueryName;
            pA.Value = r.ResolvedAddress;
            pTs.Value = ToIso(r.LastSeen == default ? DateTime.UtcNow : r.LastSeen);
            pC.Value = r.Count <= 0 ? 1 : r.Count;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<DnsResolution> GetDnsResolutions(int limit = 100000)
    {
        using var conn = Open();
        if (!TableExists(conn, "dns_resolutions")) return Array.Empty<DnsResolution>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT process_name, query_name, resolved_addr, first_seen, last_seen, sample_count " +
                          "FROM dns_resolutions ORDER BY last_seen DESC LIMIT $l;";
        cmd.Parameters.AddWithValue("$l", limit);
        var list = new List<DnsResolution>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DnsResolution
            {
                ProcessName = r.GetString(0), QueryName = r.GetString(1), ResolvedAddress = r.GetString(2),
                FirstSeen = FromIso(r.GetString(3)), LastSeen = FromIso(r.GetString(4)), Count = r.GetInt64(5)
            });
        return list;
    }

    /// <summary>Append resource-utilization samples.</summary>
    public void InsertMetricSamples(IReadOnlyCollection<(DateTime Ts, double Cpu, double MemMb, double Iops, double Mbps)> samples)
    {
        if (samples.Count == 0) return;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO metric_samples(timestamp,cpu_pct,mem_used_mb,disk_iops,net_mbps) " +
                          "VALUES($ts,$cpu,$mem,$io,$net);";
        var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);
        var pCpu = cmd.Parameters.Add("$cpu", SqliteType.Real);
        var pMem = cmd.Parameters.Add("$mem", SqliteType.Real);
        var pIo = cmd.Parameters.Add("$io", SqliteType.Real);
        var pNet = cmd.Parameters.Add("$net", SqliteType.Real);
        foreach (var s in samples)
        {
            pTs.Value = ToIso(s.Ts); pCpu.Value = s.Cpu; pMem.Value = s.MemMb;
            pIo.Value = s.Iops; pNet.Value = s.Mbps;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<(DateTime Ts, double Cpu, double MemMb, double Iops, double Mbps)> GetMetricSamples()
    {
        using var conn = Open();
        if (!TableExists(conn, "metric_samples")) return Array.Empty<(DateTime, double, double, double, double)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT timestamp,cpu_pct,mem_used_mb,disk_iops,net_mbps FROM metric_samples ORDER BY timestamp;";
        var list = new List<(DateTime, double, double, double, double)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((FromIso(r.GetString(0)), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4)));
        return list;
    }

    public RepositoryStats GetStats()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM connection_samples),
              (SELECT MIN(timestamp) FROM connection_samples),
              (SELECT MAX(timestamp) FROM connection_samples);
            """;
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            return new RepositoryStats
            {
                TotalSamples = r.GetInt64(0),
                Earliest = r.IsDBNull(1) ? null : FromIso(r.GetString(1)),
                Latest = r.IsDBNull(2) ? null : FromIso(r.GetString(2))
            };
        }
        return new RepositoryStats();
    }

    /// <summary>Distinct non-wildcard local addresses seen in this database — the machine's own IPs.</summary>
    public IReadOnlyList<string> GetLocalAddresses()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT local_address FROM connection_samples " +
                          "WHERE local_address NOT IN ('0.0.0.0','::','127.0.0.1','::1','');";
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Read a value from the generic meta key/value table.</summary>
    public string? GetMeta(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key=$k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Write a value into the meta key/value table (writer only).</summary>
    public void SetMeta(string key, string value)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO meta(key,value) VALUES($k,$v) " +
                          "ON CONFLICT(key) DO UPDATE SET value=$v;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>The machine that produced this database, if recorded.</summary>
    public string? MachineName => GetMeta("machine_name");

    private const string ConnColumns =
        "protocol, local_address, local_port, remote_address, remote_port, " +
        "state, direction, remote_scope, process_id, process_name, process_path, timestamp, service_name";

    private static List<ConnectionSample> ReadConnections(SqliteCommand cmd)
    {
        var list = new List<ConnectionSample>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ConnectionSample
            {
                Protocol = (Protocol)r.GetInt32(0),
                LocalAddress = r.GetString(1),
                LocalPort = r.GetInt32(2),
                RemoteAddress = r.GetString(3),
                RemotePort = r.GetInt32(4),
                State = (TcpState)r.GetInt32(5),
                Direction = (ConnectionDirection)r.GetInt32(6),
                RemoteScope = (IpScope)r.GetInt32(7),
                ProcessId = r.GetInt32(8),
                ProcessName = r.GetString(9),
                ProcessPath = r.IsDBNull(10) ? null : r.GetString(10),
                Timestamp = FromIso(r.GetString(11)),
                ServiceName = r.IsDBNull(12) ? string.Empty : r.GetString(12)
            });
        }
        return list;
    }

    private static string ToIso(DateTime dt) =>
        dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    private static DateTime FromIso(string s) =>
        DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal |
                                System.Globalization.DateTimeStyles.AssumeUniversal);

    public void Dispose() =>
        SqliteConnection.ClearPool(new SqliteConnection(_connectionString));
}

/// <summary>Summary counts and time bounds for the connection-sample store.</summary>
public sealed class RepositoryStats
{
    public long TotalSamples { get; set; }
    public DateTime? Earliest { get; set; }
    public DateTime? Latest { get; set; }
}
