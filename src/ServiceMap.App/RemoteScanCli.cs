using ServiceMap.App.Services;
using ServiceMap.Core.Storage;
using ServiceMap.Remote;

namespace ServiceMap.App;

/// <summary>
/// Headless entry point for scheduled remote scans:
///   CarrierDependenSee.App.exe remote-scan [--profile NAME | --all]
/// Runs the profile(s), writes per-host databases, registers them in the same
/// workspace the GUI Fleet view reads, and appends to a log file.
/// </summary>
internal static class RemoteScanCli
{
    public static async Task<int> Run(string[] args)
    {
        var name = ArgValue(args, "--profile");
        var all = args.Contains("--all", StringComparer.OrdinalIgnoreCase);

        var store = new RemoteProfileStore();
        var profiles = all
            ? store.LoadAll()
            : (name is not null && store.Load(name) is { } p ? new[] { p } : Array.Empty<RemoteScanProfile>());

        if (profiles.Count == 0)
        {
            Log($"No profile found (name='{name}', all={all}). Nothing to do.");
            Console.Error.WriteLine("No matching scan profile. Use --profile <name> or --all.");
            return 2;
        }

        var outputDir = DataDir("remote");
        var workspace = new WorkspaceStore(WorkspacePath());
        var multi = new MultiSourceDataAccess(workspace);

        var totalOk = 0; var totalFail = 0;
        foreach (var profile in profiles)
        {
            var targets = profile.BuildTargets();
            if (targets.Count == 0) { Log($"[{profile.Name}] no targets."); continue; }

            var svc = new RemoteScanService(outputDir);
            var results = await svc.ScanAsync(targets, profile.MaxParallel, null, CancellationToken.None);

            foreach (var r in results.Where(r => r.Success && r.StoredPath is not null))
            {
                try { multi.ImportMachine(r.StoredPath!); } catch { /* idempotent import */ }
            }
            var ok = results.Count(r => r.Success);
            var fail = results.Count - ok;
            totalOk += ok; totalFail += fail;
            Log($"[{profile.Name}] {ok} collected, {fail} failed " +
                $"({string.Join(", ", results.Where(r => r.Success).Select(r => $"{r.MachineName}:{r.Connections.Count}s/{r.Services.Count}svc"))})");
        }

        Log($"Scan complete: {totalOk} host(s) collected, {totalFail} failed.");
        Console.WriteLine($"Done: {totalOk} collected, {totalFail} failed.");
        return 0;
    }

    private static string? ArgValue(string[] args, string flag)
    {
        var i = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Log(string message)
    {
        try
        {
            var path = Path.Combine(DataDir(""), "remote-scan.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { /* best effort */ }
    }

    private static string DataDir(string leaf)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CarrierDependenSee", leaf);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WorkspacePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CarrierDependenSee");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "workspace.db");
    }
}
