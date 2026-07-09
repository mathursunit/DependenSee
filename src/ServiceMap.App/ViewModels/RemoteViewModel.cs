using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServiceMap.App.Services;
using ServiceMap.Remote;
using ServiceMap.Remote.Models;

namespace ServiceMap.App.ViewModels;

/// <summary>
/// Agentless remote collection: point at hosts / ranges with credentials and
/// gather services + connections over WinRM (Windows) or SSH (Linux). Each host
/// is written to its own database and imported into the Fleet view.
/// </summary>
public sealed partial class RemoteViewModel : ViewModelBase
{
    private readonly MultiSourceDataAccess _multi;
    private readonly Action _onImported;
    private readonly string _outputDir;
    private CancellationTokenSource? _cts;
    private readonly RemoteProfileStore _profiles = new();

    public RemoteViewModel(MultiSourceDataAccess multi, Action onImported)
    {
        _multi = multi;
        _onImported = onImported;
        _outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CarrierDependenSee", "remote");
    }

    public string[] OsOptions { get; } = { "Auto", "Windows (WinRM)", "Linux (SSH)" };
    [ObservableProperty] private string _selectedOs = "Auto";

    [ObservableProperty] private string _targets = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string? _password;
    [ObservableProperty] private string? _privateKeyPath;
    [ObservableProperty] private string _portText = string.Empty;
    [ObservableProperty] private int _maxParallel = 8;
    [ObservableProperty] private int _sweepsPerScan = 3;
    [ObservableProperty] private int _sweepDelaySeconds = 10;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _status = "Enter one or more hosts, ranges (10.0.0.1-50) or CIDR (10.0.0.0/24).";

    [ObservableProperty] private string _profileName = "fleet-scan";
    [ObservableProperty] private int _scheduleMinutes = 60;
    [ObservableProperty] private string _scheduleStatus = "Save the current scan as a scheduled task that runs even when this app is closed.";

    public ObservableCollection<string> Log { get; } = new();

    private OsKind Os => SelectedOs.StartsWith("Windows") ? OsKind.Windows
                       : SelectedOs.StartsWith("Linux") ? OsKind.Linux
                       : OsKind.Auto;

    [RelayCommand]
    private async Task Scan()
    {
        if (IsScanning) return;
        var hosts = TargetExpander.Expand(Targets);
        if (hosts.Count == 0) { Status = "No valid hosts to scan."; return; }
        if (string.IsNullOrWhiteSpace(Username)) { Status = "A username is required."; return; }

        int.TryParse(PortText, out var port);
        var targets = hosts.Select(h => new RemoteTarget
        {
            Host = h,
            Os = Os,
            Port = port,
            Username = Username,
            Password = Password,
            PrivateKeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath,
            SweepCount = SweepsPerScan,
            SweepDelaySeconds = SweepDelaySeconds
        }).ToList();

        Log.Clear();
        IsScanning = true;
        Status = $"Scanning {targets.Count} host(s)…";
        _cts = new CancellationTokenSource();

        var progress = new Progress<ScanProgress>(p =>
            Dispatcher.UIThread.Post(() =>
            {
                Log.Add($"[{p.Index}/{p.Total}] {p.Host}" +
                        (p.MachineName.Length > 0 && p.MachineName != p.Host ? $" ({p.MachineName})" : "") +
                        $" — {(p.Success ? "OK" : "FAILED")}: {p.Message}");
            }));

        try
        {
            var svc = new RemoteScanService(_outputDir);
            var results = await Task.Run(() =>
                svc.ScanAsync(targets, MaxParallel, progress, _cts.Token)).ConfigureAwait(true);

            var ok = 0;
            foreach (var r in results.Where(r => r.Success && r.StoredPath is not null))
            {
                try { _multi.ImportMachine(r.StoredPath!); ok++; }
                catch (Exception ex) { Log.Add($"  import failed for {r.MachineName}: {ex.Message}"); }
            }
            _onImported();
            var failed = results.Count(r => !r.Success);
            Status = $"Done. {ok} host(s) collected and added to Fleet, {failed} failed.";
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            Status = "Scan error: " + ex.Message;
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private string TaskName => "CarrierDependenSee Scan - " + ProfileName;

    private string OsShort => SelectedOs.StartsWith("Windows") ? "Windows"
                            : SelectedOs.StartsWith("Linux") ? "Linux" : "Auto";

    [RelayCommand]
    private void Schedule()
    {
        if (string.IsNullOrWhiteSpace(ProfileName)) { ScheduleStatus = "Enter a profile name."; return; }
        if (string.IsNullOrWhiteSpace(Targets)) { ScheduleStatus = "Enter hosts to scan first."; return; }
        if (string.IsNullOrWhiteSpace(Username)) { ScheduleStatus = "A username is required."; return; }

        int.TryParse(PortText, out var port);
        string? enc = null;
        if (!string.IsNullOrEmpty(Password) && CredentialProtector.IsSupported)
        {
            try { enc = CredentialProtector.Protect(Password!); }
            catch (Exception ex) { ScheduleStatus = "Could not encrypt password: " + ex.Message; return; }
        }

        var profile = new RemoteScanProfile
        {
            Name = ProfileName, Targets = Targets, Os = OsShort, Port = port,
            Username = Username, KeyPath = string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath,
            EncryptedPassword = enc, MaxParallel = MaxParallel,
            SweepCount = SweepsPerScan, SweepDelaySeconds = SweepDelaySeconds
        };
        try { _profiles.Save(profile); }
        catch (Exception ex) { ScheduleStatus = "Could not save profile: " + ex.Message; return; }

        var exe = Environment.ProcessPath ?? "CarrierDependenSee.App.exe";
        var minutes = Math.Max(1, ScheduleMinutes);
        var tr = "\"" + exe + "\" remote-scan --profile \"" + ProfileName + "\"";
        var args = "/Create /TN \"" + TaskName + "\" /TR \"" + tr.Replace("\"", "\\\"") + "\" /SC MINUTE /MO " + minutes + " /F";
        var (code, output) = RunSchtasks(args);
        ScheduleStatus = code == 0
            ? "Scheduled \"" + TaskName + "\" to run every " + minutes + " min (even when this app is closed)."
            : "Could not create scheduled task (code " + code + "). " + output;
    }

    [RelayCommand]
    private void Unschedule()
    {
        var (code, output) = RunSchtasks("/Delete /TN \"" + TaskName + "\" /F");
        try { _profiles.Delete(ProfileName); } catch { /* ignore */ }
        ScheduleStatus = code == 0 ? "Removed scheduled task \"" + TaskName + "\"." : "No matching task to remove. " + output;
    }

    private static (int Code, string Output) RunSchtasks(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe", Arguments = arguments,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            var outp = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit(15000);
            return (proc.ExitCode, outp.Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}
