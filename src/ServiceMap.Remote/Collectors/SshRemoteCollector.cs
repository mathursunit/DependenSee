using System.Diagnostics;
using Renci.SshNet;
using ServiceMap.Remote.Models;
using ServiceMap.Remote.Parsing;

namespace ServiceMap.Remote.Collectors;

/// <summary>Collects from a Linux host over SSH using a single combined command.</summary>
public sealed class SshRemoteCollector : IRemoteCollector
{
    public OsKind Handles => OsKind.Linux;

    public async Task<RemoteResult> CollectAsync(RemoteTarget target, CancellationToken ct)
    {
        var res = new RemoteResult { Host = target.Host, Os = OsKind.Linux };
        var sw = Stopwatch.StartNew();
        try
        {
            var port = target.ResolvedPort(ssh: true);
            var auth = new List<AuthenticationMethod>();
            if (!string.IsNullOrWhiteSpace(target.PrivateKeyPath))
                auth.Add(new PrivateKeyAuthenticationMethod(target.Username, new PrivateKeyFile(target.PrivateKeyPath)));
            else
                auth.Add(new PasswordAuthenticationMethod(target.Username, target.Password ?? string.Empty));

            var ci = new ConnectionInfo(target.Host, port, target.Username, auth.ToArray())
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            using var client = new SshClient(ci);
            await Task.Run(() => client.Connect(), ct).ConfigureAwait(false);

            // Several sweeps within the one session: each is a fresh snapshot,
            // so short-lived flows have several chances to be caught. Services
            // are recorded once (they change on a much slower timescale).
            var sweeps = Math.Clamp(target.SweepCount, 1, 10);
            var delay = TimeSpan.FromSeconds(Math.Clamp(target.SweepDelaySeconds, 1, 60));
            for (var i = 0; i < sweeps; i++)
            {
                if (i > 0) await Task.Delay(delay, ct).ConfigureAwait(false);

                string output;
                using (var cmd = client.CreateCommand(SshOutputParser.Command))
                {
                    output = await Task.Run(() => cmd.Execute(), ct).ConfigureAwait(false);
                }

                var parsed = SshOutputParser.Parse(output, DateTime.UtcNow);
                if (i == 0)
                {
                    res.MachineName = string.IsNullOrWhiteSpace(parsed.MachineName) ? target.Host : parsed.MachineName;
                    res.Services.AddRange(parsed.Services);
                }
                res.Connections.AddRange(parsed.Connections);
            }
            client.Disconnect();

            foreach (var c in res.Connections) c.Machine = res.MachineName;
            res.SweepCount = sweeps;
            res.Success = true;
        }
        catch (Exception ex)
        {
            res.Success = false;
            res.Error = ex.Message;
        }
        res.DurationMs = sw.ElapsedMilliseconds;
        return res;
    }
}
