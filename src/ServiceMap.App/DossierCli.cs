using ServiceMap.App.Services;
using ServiceMap.Core.Storage;

namespace ServiceMap.App;

/// <summary>
/// Headless dossier export:
///   CarrierDependenSee.App.exe export-dossier [--machine NAME | --all | --local]
///                                             [--hours N] [--out DIR]
/// Uses the same workspace, settings, and policy folder as the GUI. Combined
/// with a scheduled task this produces nightly dossiers with zero clicks.
/// </summary>
internal static class DossierCli
{
    public static int Run(string[] args)
    {
        var settings = AppSettings.Load();
        var machineArg = ArgValue(args, "--machine");
        var all = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
        var local = args.Contains("--local", StringComparer.OrdinalIgnoreCase);
        var hours = int.TryParse(ArgValue(args, "--hours"), out var h) ? Math.Max(1, h) : 168;
        var outDir = ArgValue(args, "--out") ?? settings.ExportDirectory;
        Directory.CreateDirectory(outDir);

        var workspace = new WorkspaceStore(WorkspacePath());
        var multi = new MultiSourceDataAccess(workspace);
        var machines = multi.GetMachines();

        var targets = new List<(string Name, string DbPath, string Wave)>();
        if (local || (!all && machineArg is null))
        {
            if (File.Exists(settings.DatabasePath))
                targets.Add((Environment.MachineName, settings.DatabasePath, ""));
            else if (!all && machineArg is null)
            {
                Console.Error.WriteLine(
                    "No local collector database found. Use --machine <name> or --all for fleet machines.");
                return 2;
            }
        }
        if (all)
            targets.AddRange(machines.Select(m => (m.Name, m.DatabasePath, m.Wave)));
        else if (machineArg is not null)
        {
            var m = machines.FirstOrDefault(x =>
                x.Name.Equals(machineArg, StringComparison.OrdinalIgnoreCase));
            if (m is null)
            {
                Console.Error.WriteLine($"Machine '{machineArg}' is not in the fleet workspace. Known: " +
                    string.Join(", ", machines.Select(x => x.Name)));
                return 2;
            }
            targets.Add((m.Name, m.DatabasePath, m.Wave));
        }

        var failed = 0;
        foreach (var (name, dbPath, wave) in targets.DistinctBy(t => t.DbPath))
        {
            var zip = Path.Combine(outDir, $"{Sanitize(name)}-dossier-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            try
            {
                Console.WriteLine($"[{name}] exporting…");
                var progress = new Progress<(int Percent, string Stage)>(p =>
                    Console.WriteLine($"[{name}] {p.Percent,3}% {p.Stage}"));
                DossierExporter.Export(zip, name, dbPath, wave, hours, multi,
                    workspace.GetAnnotationLookup(), settings.FirewallPolicyFolder,
                    logoPath: null, progress);
                Console.WriteLine($"[{name}] done: {zip}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"[{name}] FAILED: {ex.Message}");
            }
        }
        Console.WriteLine($"Dossiers: {targets.Count - failed} exported, {failed} failed → {outDir}");
        return failed == 0 ? 0 : 1;
    }

    private static string? ArgValue(string[] args, string flag)
    {
        var i = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 0 ? name : "server";
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
