using System.Text.Json;
using ServiceMap.Core;

namespace ServiceMap.App.Services;

/// <summary>
/// GUI-side settings persisted to the user's local app data. Kept separate from
/// the collector's appsettings.json since the reader runs as a normal user.
/// </summary>
public sealed class AppSettings
{
    public string DatabasePath { get; set; } = new CollectorOptions().DatabasePath;
    public int RefreshIntervalSeconds { get; set; } = 5;
    public string ExportDirectory { get; set; } = new CollectorOptions().ExportDirectory;
    public string FirewallPolicyFolder { get; set; } = string.Empty;

    /// <summary>After a successful export, open an Explorer window with the file selected.</summary>
    public bool OpenFolderAfterExport { get; set; } = true;

    private static string SettingsPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CarrierDependenSee");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "gui-settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // Fall back to defaults on any read/parse error.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Non-fatal: settings persistence is best-effort.
        }
    }
}
