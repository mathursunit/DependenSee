using ServiceMap.Core.Storage;
using Xunit;

namespace ServiceMap.Tests;

/// <summary>Machine DB paths are stored relative to the workspace so a project folder is portable.</summary>
public sealed class WorkspacePortableTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cds-ws-{Guid.NewGuid():N}");

    public WorkspacePortableTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void MachineUnderWorkspaceResolvesAfterMove()
    {
        // Project A: workspace + a machine db under a 'remote' subfolder.
        var dirA = Path.Combine(_dir, "A");
        Directory.CreateDirectory(Path.Combine(dirA, "remote"));
        var dbA = Path.Combine(dirA, "remote", "SQL01.db");
        File.WriteAllText(dbA, "x");
        long id;
        {
            var wsA = new WorkspaceStore(Path.Combine(dirA, "workspace.db"));
            id = wsA.AddMachine("SQL01", dbA);
            Assert.Equal(dbA, wsA.GetMachines().Single().DatabasePath);   // resolved back to absolute
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Move the whole project folder A -> B; paths must still resolve.
        var dirB = Path.Combine(_dir, "B");
        Directory.Move(dirA, dirB);
        var wsB = new WorkspaceStore(Path.Combine(dirB, "workspace.db"));
        var m = wsB.GetMachines().Single();
        Assert.Equal(Path.Combine(dirB, "remote", "SQL01.db"), m.DatabasePath);
        Assert.True(File.Exists(m.DatabasePath));
    }

    [Fact]
    public void MachineOutsideWorkspaceStaysAbsolute()
    {
        var outside = Path.Combine(_dir, "elsewhere.db");
        File.WriteAllText(outside, "x");
        var ws = new WorkspaceStore(Path.Combine(_dir, "proj", "workspace.db"));
        ws.AddMachine("X", outside);
        Assert.Equal(Path.GetFullPath(outside), ws.GetMachines().Single().DatabasePath);
    }

    [Fact]
    public void ReAddIsIdempotentAcrossPathForms()
    {
        var dir = Path.Combine(_dir, "P");
        Directory.CreateDirectory(dir);
        var db = Path.Combine(dir, "m.db");
        File.WriteAllText(db, "x");
        var ws = new WorkspaceStore(Path.Combine(dir, "workspace.db"));
        var id1 = ws.AddMachine("M", db);
        var id2 = ws.AddMachine("M", Path.GetFullPath(db));
        Assert.Equal(id1, id2);
        Assert.Single(ws.GetMachines());
    }
}
