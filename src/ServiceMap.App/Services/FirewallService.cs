using ServiceMap.Firewall.Matching;

namespace ServiceMap.App.Services;

/// <summary>
/// Loads and caches a firewall policy from a folder of CSV exports. All files
/// matching each category keyword are merged (multi-region Panorama exports,
/// several Check Point packages), and the loaded file names are surfaced so
/// it's obvious what the reconciliation is based on.
/// </summary>
public sealed class FirewallService
{
    public FirewallPolicy? Policy { get; private set; }
    public string? Error { get; private set; }
    public bool IsLoaded => Policy is not null;

    /// <summary>File names (no path) loaded on the last successful Load.</summary>
    public IReadOnlyList<string> LoadedFiles { get; private set; } = Array.Empty<string>();

    public bool Load(string folder)
    {
        Error = null;
        try
        {
            if (!Directory.Exists(folder)) { Error = "Folder not found."; return false; }

            var all = Directory.EnumerateFiles(folder, "*.csv").OrderBy(f => f).ToList();
            List<string> Files(string keyword) => all
                .Where(f => Path.GetFileName(f).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
            IReadOnlyList<string> Texts(List<string> files) => files.Select(File.ReadAllText).ToList();

            var egress = Files("Egress");
            var ingress = Files("Ingress");
            var checkpoint = Files("checkpoint");
            var groups = Files("address_group");

            Policy = FirewallPolicy.Load(
                Texts(egress), Texts(ingress), Texts(checkpoint), Texts(groups));
            LoadedFiles = egress.Concat(ingress).Concat(checkpoint).Concat(groups)
                .Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();
            return true;
        }
        catch (Exception ex)
        {
            Policy = null;
            LoadedFiles = Array.Empty<string>();
            Error = ex.Message;
            return false;
        }
    }
}
