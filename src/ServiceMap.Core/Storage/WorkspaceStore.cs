using Microsoft.Data.Sqlite;
using ServiceMap.Core.Models;

namespace ServiceMap.Core.Storage;

/// <summary>
/// GUI-owned metadata database (annotations, imported machines, baselines,
/// wave assignments). Kept separate from the collector's read-only sample
/// database so the viewer can write to it freely.
/// </summary>
public sealed class WorkspaceStore
{
    private readonly string _cs;

    private readonly string _baseDir;

    public WorkspaceStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _baseDir = dir ?? Directory.GetCurrentDirectory();
        _cs = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = true }.ToString();
        Initialize();
    }

    /// <summary>
    /// Store machine database paths relative to the workspace folder when the
    /// database lives under it, so the whole project folder (workspace.db +
    /// remote\*.db) can be moved to a share or USB stick and still resolve.
    /// Absolute paths outside the folder are stored as-is.
    /// </summary>
    private string ToStoredPath(string dbPath)
    {
        try
        {
            var full = Path.GetFullPath(dbPath);
            var rel = Path.GetRelativePath(_baseDir, full);
            // Only relativize when the db is inside the workspace folder tree.
            return rel.StartsWith("..") || Path.IsPathRooted(rel) ? full : rel;
        }
        catch { return dbPath; }
    }

    private string ResolvePath(string stored) =>
        Path.IsPathRooted(stored) ? stored : Path.GetFullPath(Path.Combine(_baseDir, stored));

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS annotations (
                kind        INTEGER NOT NULL,
                key         TEXT NOT NULL,
                friendly    TEXT,
                owner       TEXT,
                criticality INTEGER NOT NULL DEFAULT 0,
                notes       TEXT,
                updated     TEXT NOT NULL,
                PRIMARY KEY (kind, key)
            );
            CREATE TABLE IF NOT EXISTS machines (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                name      TEXT NOT NULL,
                db_path   TEXT NOT NULL,
                wave      TEXT NOT NULL DEFAULT '',
                added     TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS baselines (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                name     TEXT NOT NULL,
                machine  TEXT NOT NULL DEFAULT '',
                created  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS baseline_flows (
                baseline_id  INTEGER NOT NULL,
                protocol     INTEGER NOT NULL,
                direction    INTEGER NOT NULL,
                scope        INTEGER NOT NULL,
                local_addr   TEXT NOT NULL,
                local_port   INTEGER NOT NULL,
                remote_addr  TEXT NOT NULL,
                remote_port  INTEGER NOT NULL,
                process      TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_bf_baseline ON baseline_flows(baseline_id);
            """;
        cmd.ExecuteNonQuery();
    }

    // ---- Annotations ----

    public void Upsert(Annotation a)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO annotations(kind,key,friendly,owner,criticality,notes,updated)
            VALUES($kind,$key,$fr,$ow,$cr,$no,$up)
            ON CONFLICT(kind,key) DO UPDATE SET
              friendly=$fr, owner=$ow, criticality=$cr, notes=$no, updated=$up;
            """;
        cmd.Parameters.AddWithValue("$kind", (int)a.Kind);
        cmd.Parameters.AddWithValue("$key", a.Key);
        cmd.Parameters.AddWithValue("$fr", (object?)a.FriendlyName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ow", (object?)a.Owner ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cr", (int)a.Criticality);
        cmd.Parameters.AddWithValue("$no", (object?)a.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$up", Iso(DateTime.UtcNow));
        cmd.ExecuteNonQuery();
    }

    public void DeleteAnnotation(AnnotationKind kind, string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM annotations WHERE kind=$k AND key=$key;";
        cmd.Parameters.AddWithValue("$k", (int)kind);
        cmd.Parameters.AddWithValue("$key", key);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<Annotation> GetAnnotations()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT kind,key,friendly,owner,criticality,notes,updated FROM annotations ORDER BY kind,key;";
        var list = new List<Annotation>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Annotation
            {
                Kind = (AnnotationKind)r.GetInt32(0),
                Key = r.GetString(1),
                FriendlyName = r.IsDBNull(2) ? null : r.GetString(2),
                Owner = r.IsDBNull(3) ? null : r.GetString(3),
                Criticality = (Criticality)r.GetInt32(4),
                Notes = r.IsDBNull(5) ? null : r.GetString(5),
                Updated = From(r.GetString(6))
            });
        return list;
    }

    /// <summary>Lookup keyed by "kind:key" for fast enrichment.</summary>
    public Dictionary<string, Annotation> GetAnnotationLookup()
    {
        var d = new Dictionary<string, Annotation>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in GetAnnotations()) d[$"{(int)a.Kind}:{a.Key}"] = a;
        return d;
    }

    // ---- Machines (fleet) ----

    public long AddMachine(string name, string dbPath)
    {
        using var conn = Open();

        // Idempotent: if this database is already registered, update its name and
        // reuse the existing row instead of inserting a duplicate (re-scans reuse
        // the same per-host db file).
        var stored = ToStoredPath(dbPath);
        using (var find = conn.CreateCommand())
        {
            find.CommandText = "SELECT id FROM machines WHERE db_path=$p OR db_path=$abs LIMIT 1;";
            find.Parameters.AddWithValue("$p", stored);
            find.Parameters.AddWithValue("$abs", Path.GetFullPath(dbPath));
            var existing = find.ExecuteScalar();
            if (existing is not null && existing is not DBNull)
            {
                var id = Convert.ToInt64(existing);
                using var upd = conn.CreateCommand();
                upd.CommandText = "UPDATE machines SET name=$n WHERE id=$id;";
                upd.Parameters.AddWithValue("$n", name);
                upd.Parameters.AddWithValue("$id", id);
                upd.ExecuteNonQuery();
                return id;
            }
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO machines(name,db_path,wave,added) VALUES($n,$p,'',$a); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$p", stored);
        cmd.Parameters.AddWithValue("$a", Iso(DateTime.UtcNow));
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public void RemoveMachine(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM machines WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetWave(long machineId, string wave)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE machines SET wave=$w WHERE id=$id;";
        cmd.Parameters.AddWithValue("$w", wave ?? "");
        cmd.Parameters.AddWithValue("$id", machineId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<MachineRef> GetMachines()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,name,db_path,wave,added FROM machines ORDER BY wave,name;";
        var list = new List<MachineRef>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MachineRef
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                DatabasePath = ResolvePath(r.GetString(2)),
                Wave = r.GetString(3),
                Added = From(r.GetString(4))
            });
        return list;
    }

    // ---- Baselines ----

    public long SaveBaseline(string name, string machine, IEnumerable<ConnectionAggregate> flows)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        long id;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO baselines(name,machine,created) VALUES($n,$m,$c); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$m", machine ?? "");
            cmd.Parameters.AddWithValue("$c", Iso(DateTime.UtcNow));
            id = (long)(cmd.ExecuteScalar() ?? 0L);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO baseline_flows
                    (baseline_id,protocol,direction,scope,local_addr,local_port,remote_addr,remote_port,process)
                VALUES($b,$pr,$dir,$sc,$la,$lp,$ra,$rp,$proc);
                """;
            var b = cmd.Parameters.Add("$b", SqliteType.Integer);
            var pr = cmd.Parameters.Add("$pr", SqliteType.Integer);
            var dir = cmd.Parameters.Add("$dir", SqliteType.Integer);
            var sc = cmd.Parameters.Add("$sc", SqliteType.Integer);
            var la = cmd.Parameters.Add("$la", SqliteType.Text);
            var lp = cmd.Parameters.Add("$lp", SqliteType.Integer);
            var ra = cmd.Parameters.Add("$ra", SqliteType.Text);
            var rp = cmd.Parameters.Add("$rp", SqliteType.Integer);
            var proc = cmd.Parameters.Add("$proc", SqliteType.Text);
            foreach (var f in flows)
            {
                b.Value = id; pr.Value = (int)f.Protocol; dir.Value = (int)f.Direction;
                sc.Value = (int)f.RemoteScope; la.Value = f.LocalAddress; lp.Value = f.LocalPort;
                ra.Value = f.RemoteAddress; rp.Value = f.RemotePort; proc.Value = f.ProcessName;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
        return id;
    }

    public void DeleteBaseline(long id)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var sql in new[] { "DELETE FROM baseline_flows WHERE baseline_id=$id;",
                                    "DELETE FROM baselines WHERE id=$id;" })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx; cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<Baseline> GetBaselines()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT b.id,b.name,b.machine,b.created,
                   (SELECT COUNT(*) FROM baseline_flows f WHERE f.baseline_id=b.id)
            FROM baselines b ORDER BY b.created DESC;
            """;
        var list = new List<Baseline>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Baseline
            {
                Id = r.GetInt64(0), Name = r.GetString(1), Machine = r.GetString(2),
                Created = From(r.GetString(3)), FlowCount = r.GetInt32(4)
            });
        return list;
    }

    public IReadOnlyList<ConnectionAggregate> GetBaselineFlows(long baselineId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT protocol,direction,scope,local_addr,local_port,remote_addr,remote_port,process
            FROM baseline_flows WHERE baseline_id=$id;
            """;
        cmd.Parameters.AddWithValue("$id", baselineId);
        var list = new List<ConnectionAggregate>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ConnectionAggregate
            {
                Protocol = (Protocol)r.GetInt32(0),
                Direction = (ConnectionDirection)r.GetInt32(1),
                RemoteScope = (IpScope)r.GetInt32(2),
                LocalAddress = r.GetString(3),
                LocalPort = r.GetInt32(4),
                RemoteAddress = r.GetString(5),
                RemotePort = r.GetInt32(6),
                ProcessName = r.GetString(7)
            });
        return list;
    }

    /// <summary>Canonical identity of a flow for baseline/diff comparison.</summary>
    public static string FlowKey(ConnectionAggregate f) =>
        $"{(int)f.Protocol}|{(int)f.Direction}|{f.ProcessName}|{f.LocalAddress}|{f.LocalPort}|{f.RemoteAddress}|{f.RemotePort}";

    public static BaselineDiff Diff(IEnumerable<ConnectionAggregate> baseline, IEnumerable<ConnectionAggregate> current)
    {
        var baseSet = baseline.ToDictionary(FlowKey, x => x);
        var curSet = current.ToDictionary(FlowKey, x => x);
        var diff = new BaselineDiff();
        foreach (var (k, v) in curSet)
            if (!baseSet.ContainsKey(k)) diff.Added.Add(v); else diff.UnchangedCount++;
        foreach (var (k, v) in baseSet)
            if (!curSet.ContainsKey(k)) diff.Removed.Add(v);
        return diff;
    }

    private static string Iso(DateTime dt) => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    private static DateTime From(string s) => DateTime.Parse(s, null,
        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
}
