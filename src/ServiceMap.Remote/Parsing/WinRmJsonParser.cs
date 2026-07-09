using System.Text.Json;
using ServiceMap.Core.Models;

namespace ServiceMap.Remote.Parsing;

/// <summary>
/// Parses the JSON emitted by the remote PowerShell payload (see
/// <see cref="Collectors.WinRmRemoteCollector"/>) into services and connection
/// samples, resolving each socket to its owning process and Windows service.
/// </summary>
public static class WinRmJsonParser
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public sealed class Parsed
    {
        public string MachineName = string.Empty;
        public List<ServiceRecord> Services = new();
        public List<ConnectionSample> Connections = new();
    }

    public static Parsed Parse(string json, DateTime timestampUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var parsed = new Parsed
        {
            MachineName = GetString(root, "host")
        };

        // Process id -> (name, path)
        var procName = new Dictionary<int, string>();
        var procPath = new Dictionary<int, string>();
        foreach (var p in Array(root, "procs"))
        {
            var pid = GetInt(p, "Pid");
            if (pid <= 0) continue;
            procName[pid] = GetString(p, "Name");
            var path = GetString(p, "Path");
            if (path.Length > 0) procPath[pid] = path;
        }

        // Services + PID -> owning service map
        var pidToService = new Dictionary<int, string>();
        foreach (var s in Array(root, "services"))
        {
            var name = GetString(s, "Name");
            var display = GetString(s, "DisplayName");
            var pid = GetInt(s, "Pid");
            parsed.Services.Add(new ServiceRecord
            {
                Name = name,
                DisplayName = display.Length > 0 ? display : name,
                State = GetString(s, "State"),
                StartMode = GetString(s, "StartMode"),
                ProcessId = pid,
                ExecutablePath = NullIfEmpty(GetString(s, "Path")),
                Account = NullIfEmpty(GetString(s, "Account")),
                ScanTimestamp = timestampUtc
            });
            if (pid > 0)
            {
                var label = display.Length > 0 ? display : name;
                pidToService[pid] = pidToService.TryGetValue(pid, out var e) ? e + ", " + label : label;
            }
        }

        foreach (var t in Array(root, "tcp"))
        {
            var pid = GetInt(t, "Pid");
            var state = MapTcpState(GetString(t, "State"));
            var remote = GetString(t, "RemoteAddress");
            var rport = GetInt(t, "RemotePort");
            var listener = state == TcpState.Listen;
            var sample = new ConnectionSample
            {
                Protocol = Protocol.Tcp,
                LocalAddress = GetString(t, "LocalAddress"),
                LocalPort = GetInt(t, "LocalPort"),
                RemoteAddress = listener ? string.Empty : remote,
                RemotePort = listener ? 0 : rport,
                State = state,
                ProcessId = pid,
                Timestamp = timestampUtc
            };
            Attribute(sample, pid, procName, procPath, pidToService);
            parsed.Connections.Add(sample);
        }

        foreach (var u in Array(root, "udp"))
        {
            var pid = GetInt(u, "Pid");
            var sample = new ConnectionSample
            {
                Protocol = Protocol.Udp,
                LocalAddress = GetString(u, "LocalAddress"),
                LocalPort = GetInt(u, "LocalPort"),
                State = TcpState.Unknown,
                ProcessId = pid,
                Timestamp = timestampUtc
            };
            Attribute(sample, pid, procName, procPath, pidToService);
            parsed.Connections.Add(sample);
        }

        DirectionResolver.Assign(parsed.Connections);
        return parsed;
    }

    private static void Attribute(ConnectionSample s, int pid,
        Dictionary<int, string> procName, Dictionary<int, string> procPath,
        Dictionary<int, string> pidToService)
    {
        if (pid <= 0) return;
        if (procName.TryGetValue(pid, out var n)) s.ProcessName = n;
        if (procPath.TryGetValue(pid, out var p)) s.ProcessPath = p;
        if (pidToService.TryGetValue(pid, out var svc)) s.ServiceName = svc;
    }

    private static TcpState MapTcpState(string s) => s.Replace("-", string.Empty).ToLowerInvariant() switch
    {
        "listen" => TcpState.Listen,
        "established" => TcpState.Established,
        "timewait" => TcpState.TimeWait,
        "closewait" => TcpState.CloseWait,
        "synsent" => TcpState.SynSent,
        "synreceived" => TcpState.SynReceived,
        "finwait1" => TcpState.FinWait1,
        "finwait2" => TcpState.FinWait2,
        "lastack" => TcpState.LastAck,
        "closing" => TcpState.Closing,
        "closed" => TcpState.Closed,
        _ => TcpState.Unknown
    };

    private static IEnumerable<JsonElement> Array(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static string GetString(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return string.Empty;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? string.Empty,
            JsonValueKind.Number => v.ToString(),
            _ => string.Empty
        };
    }

    private static int GetInt(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var i) ? i : 0,
            JsonValueKind.String => int.TryParse(v.GetString(), out var j) ? j : 0,
            _ => 0
        };
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
}
