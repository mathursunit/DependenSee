using System.Diagnostics;
using System.Linq;
using System.Text;
using ServiceMap.Remote.Models;
using ServiceMap.Remote.Parsing;

namespace ServiceMap.Remote.Collectors;

/// <summary>
/// Collects from a Windows host over WinRM by driving the local Windows
/// PowerShell (Invoke-Command with -Credential). The remote payload emits JSON,
/// which <see cref="WinRmJsonParser"/> turns into services and samples.
/// Credentials are passed via environment variables, never on the command line.
/// </summary>
public sealed class WinRmRemoteCollector : IRemoteCollector
{
    public OsKind Handles => OsKind.Windows;

    // Runs on the REMOTE host inside Invoke-Command. Takes several snapshots
    // ({SWEEPS} sweeps, {DELAY}s apart) within the single session and emits one
    // compact JSON document per sweep, one per line. Services and processes are
    // gathered once up front (Win32_Process is the expensive query) and
    // repeated on every line so each sweep parses with full attribution.
    private const string RemotePayloadTemplate = @"
$ErrorActionPreference='SilentlyContinue'
$services = @(Get-CimInstance Win32_Service | ForEach-Object { [pscustomobject]@{ Name=$_.Name; DisplayName=$_.DisplayName; State=$_.State; StartMode=$_.StartMode; Pid=[int]$_.ProcessId; Path=$_.PathName; Account=$_.StartName } })
$procs = @(Get-CimInstance Win32_Process | ForEach-Object { [pscustomobject]@{ Pid=[int]$_.ProcessId; Name=$_.Name; Path=$_.ExecutablePath } })
for ($cdsI = 0; $cdsI -lt {SWEEPS}; $cdsI++) {
  if ($cdsI -gt 0) { Start-Sleep -Seconds {DELAY} }
  $tcp = @(Get-NetTCPConnection -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ LocalAddress=[string]$_.LocalAddress; LocalPort=[int]$_.LocalPort; RemoteAddress=[string]$_.RemoteAddress; RemotePort=[int]$_.RemotePort; State=[string]$_.State; Pid=[int]$_.OwningProcess } })
  $udp = @(Get-NetUDPEndpoint -ErrorAction SilentlyContinue | ForEach-Object { [pscustomobject]@{ LocalAddress=[string]$_.LocalAddress; LocalPort=[int]$_.LocalPort; Pid=[int]$_.OwningProcess } })
  [pscustomobject]@{ host=$env:COMPUTERNAME; services=$services; tcp=$tcp; udp=$udp; procs=$procs } | ConvertTo-Json -Depth 4 -Compress
}
";

    // Runs LOCALLY: builds the credential and invokes the payload remotely.
    // ProgressPreference is silenced so PowerShell doesn't emit CLIXML progress
    // records on stderr; errors are caught and written as a clean CDSERR line.
    private const string LocalWrapper = @"
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
try {
  $sec = ConvertTo-SecureString $env:CDS_PW -AsPlainText -Force
  $cred = New-Object System.Management.Automation.PSCredential($env:CDS_USER, $sec)
  $sb = [ScriptBlock]::Create($env:CDS_PAYLOAD)
  $a = @{ ComputerName=$env:CDS_HOST; Credential=$cred; ScriptBlock=$sb; ErrorAction='Stop' }
  if ($env:CDS_PORT) { $a['Port'] = [int]$env:CDS_PORT }
  $out = Invoke-Command @a
  # One JSON document per sweep, one per line (casting an array to string
  # would join with spaces and corrupt the line framing).
  foreach ($line in @($out)) { [Console]::Out.WriteLine([string]$line) }
} catch {
  [Console]::Error.Write('CDSERR:' + $_.Exception.Message)
  exit 1
}
";

    public async Task<RemoteResult> CollectAsync(RemoteTarget target, CancellationToken ct)
    {
        var res = new RemoteResult { Host = target.Host, Os = OsKind.Windows };
        var sw = Stopwatch.StartNew();
        try
        {
            var sweeps = Math.Clamp(target.SweepCount, 1, 10);
            var delay = Math.Clamp(target.SweepDelaySeconds, 1, 60);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(LocalWrapper));
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["CDS_HOST"] = target.Host;
            psi.Environment["CDS_USER"] = target.Username;
            psi.Environment["CDS_PW"] = target.Password ?? string.Empty;
            psi.Environment["CDS_PAYLOAD"] = RemotePayloadTemplate
                .Replace("{SWEEPS}", sweeps.ToString())
                .Replace("{DELAY}", delay.ToString());
            if (target.Port > 0) psi.Environment["CDS_PORT"] = target.Port.ToString();

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                res.Success = false;
                res.Error = CleanError(stderr, proc.ExitCode);
            }
            else
            {
                // One JSON document per sweep, one per line. Timestamps are
                // reconstructed by spacing sweeps backwards from completion so
                // each sweep keeps its own batch identity in the database.
                var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                  .Where(l => l.StartsWith('{')).ToArray();
                var end = DateTime.UtcNow;
                var anyAttribution = false;
                for (var i = 0; i < lines.Length; i++)
                {
                    var ts = end.AddSeconds(-(double)(lines.Length - 1 - i) * delay);
                    var parsed = WinRmJsonParser.Parse(lines[i], ts);
                    if (i == 0)
                    {
                        res.MachineName = string.IsNullOrWhiteSpace(parsed.MachineName) ? target.Host : parsed.MachineName;
                        res.Services.AddRange(parsed.Services);
                    }
                    res.Connections.AddRange(parsed.Connections);
                    anyAttribution |= parsed.Connections.Any(c =>
                        !string.IsNullOrEmpty(c.ProcessName) || !string.IsNullOrEmpty(c.ServiceName));
                }
                foreach (var c in res.Connections) c.Machine = res.MachineName;
                res.SweepCount = Math.Max(1, lines.Length);
                res.Success = lines.Length > 0;
                if (lines.Length == 0) res.Error = "remote payload returned no JSON.";

                // Diagnostic: if we got sockets but no process/service attribution,
                // stash the raw JSON so the gap can be pinpointed from real data.
                if (res.Connections.Count > 0 && !anyAttribution)
                    TryWriteDebug(target.Host, stdout);
            }
        }
        catch (Exception ex)
        {
            res.Success = false;
            res.Error = ex.Message;
        }
        res.DurationMs = sw.ElapsedMilliseconds;
        return res;
    }


    private static void TryWriteDebug(string host, string rawJson)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CarrierDependenSee", "remote-debug");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(host.Split(Path.GetInvalidFileNameChars()));
            File.WriteAllText(Path.Combine(dir, $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.json"), rawJson);
        }
        catch { /* best effort */ }
    }

    /// <summary>Turn PowerShell stderr (possibly CLIXML) into a readable message.</summary>
    private static string CleanError(string stderr, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return $"WinRM collection failed (exit {exitCode}).";

        var idx = stderr.IndexOf("CDSERR:", StringComparison.Ordinal);
        if (idx >= 0)
            return stderr[(idx + 7)..].Trim();

        // Fallback: strip CLIXML header and XML tags to recover the text.
        var text = stderr;
        var clix = text.IndexOf("#< CLIXML", StringComparison.Ordinal);
        if (clix >= 0)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " ");
            text = text.Replace("#< CLIXML", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
        }
        return text.Length > 0 ? text : $"WinRM collection failed (exit {exitCode}).";
    }
}
