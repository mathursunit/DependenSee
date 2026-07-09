using System.Text.Json;
using ServiceMap.Remote.Models;

namespace ServiceMap.Remote;

/// <summary>
/// A saved, reusable remote-scan definition for unattended (scheduled) runs.
/// Passwords are stored DPAPI-encrypted; key-based SSH profiles store no secret.
/// </summary>
public sealed class RemoteScanProfile
{
    public string Name { get; set; } = string.Empty;
    public string Targets { get; set; } = string.Empty;
    public string Os { get; set; } = "Auto";          // Auto | Windows | Linux
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? KeyPath { get; set; }
    public string? EncryptedPassword { get; set; }    // DPAPI base64, null when key-based
    public int MaxParallel { get; set; } = 8;

    /// <summary>Snapshots per session (see <see cref="RemoteTarget.SweepCount"/>).</summary>
    public int SweepCount { get; set; } = 3;

    /// <summary>Seconds between in-session sweeps.</summary>
    public int SweepDelaySeconds { get; set; } = 10;

    private OsKind OsKind => Os.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) ? OsKind.Windows
                           : Os.StartsWith("Linux", StringComparison.OrdinalIgnoreCase) ? OsKind.Linux
                           : OsKind.Auto;

    /// <summary>Expand into concrete targets, decrypting the stored password if present.</summary>
    public IReadOnlyList<RemoteTarget> BuildTargets()
    {
        string? password = null;
        if (!string.IsNullOrEmpty(EncryptedPassword) && CredentialProtector.IsSupported)
            password = CredentialProtector.Unprotect(EncryptedPassword);

        return TargetExpander.Expand(Targets).Select(h => new RemoteTarget
        {
            Host = h,
            Os = OsKind,
            Port = Port,
            Username = Username,
            Password = password,
            PrivateKeyPath = string.IsNullOrWhiteSpace(KeyPath) ? null : KeyPath,
            SweepCount = SweepCount,
            SweepDelaySeconds = SweepDelaySeconds
        }).ToList();
    }
}

/// <summary>Persists scan profiles as JSON under ProgramData, one file per profile.</summary>
public sealed class RemoteProfileStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _dir;

    public RemoteProfileStore(string? dir = null)
    {
        _dir = dir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CarrierDependenSee", "remote-profiles");
    }

    private string PathFor(string name) => Path.Combine(_dir, Sanitize(name) + ".json");

    public void Save(RemoteScanProfile profile)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor(profile.Name), JsonSerializer.Serialize(profile, Json));
    }

    public RemoteScanProfile? Load(string name)
    {
        var p = PathFor(name);
        return File.Exists(p) ? JsonSerializer.Deserialize<RemoteScanProfile>(File.ReadAllText(p)) : null;
    }

    public IReadOnlyList<RemoteScanProfile> LoadAll()
    {
        if (!Directory.Exists(_dir)) return Array.Empty<RemoteScanProfile>();
        var list = new List<RemoteScanProfile>();
        foreach (var f in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try { if (JsonSerializer.Deserialize<RemoteScanProfile>(File.ReadAllText(f)) is { } p) list.Add(p); }
            catch { /* skip malformed */ }
        }
        return list;
    }

    public void Delete(string name)
    {
        var p = PathFor(name);
        if (File.Exists(p)) File.Delete(p);
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "default" : name;
    }
}
