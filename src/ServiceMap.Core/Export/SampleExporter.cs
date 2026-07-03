using System.Globalization;
using System.Text;
using System.Text.Json;
using ServiceMap.Core.Models;

namespace ServiceMap.Core.Export;

/// <summary>Writes connection samples and distinct flows to CSV or JSON.</summary>
public static class SampleExporter
{
    public static void WriteCsv(string path, IEnumerable<ConnectionSample> samples)
    {
        EnsureDir(path);
        using var w = new StreamWriter(path, append: false, Encoding.UTF8);
        w.WriteLine("timestamp,source,protocol,direction,scope,state,local_address,local_port," +
                    "remote_address,remote_port,process_id,process_name,service_name,process_path");
        foreach (var s in samples)
        {
            w.Write(s.Timestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            w.Write(','); w.Write(Csv(s.Machine));
            w.Write(','); w.Write(s.Protocol);
            w.Write(','); w.Write(s.Direction);
            w.Write(','); w.Write(s.RemoteScope);
            w.Write(','); w.Write(s.State);
            w.Write(','); w.Write(Csv(s.LocalAddress));
            w.Write(','); w.Write(s.LocalPort);
            w.Write(','); w.Write(Csv(s.RemoteAddress));
            w.Write(','); w.Write(s.RemotePort);
            w.Write(','); w.Write(s.ProcessId);
            w.Write(','); w.Write(Csv(s.ProcessName));
            w.Write(','); w.Write(Csv(s.ServiceName));
            w.Write(','); w.Write(Csv(s.ProcessPath ?? string.Empty));
            w.WriteLine();
        }
    }

    public static void WriteJson(string path, IEnumerable<ConnectionSample> samples)
    {
        EnsureDir(path);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var payload = samples.Select(s => new
        {
            s.Timestamp,
            source = s.Machine,
            protocol = s.Protocol.ToString(),
            direction = s.Direction.ToString(),
            scope = s.RemoteScope.ToString(),
            state = s.State.ToString(),
            s.LocalAddress,
            s.LocalPort,
            s.RemoteAddress,
            s.RemotePort,
            s.ProcessId,
            s.ProcessName,
            s.ServiceName,
            s.ProcessPath
        });
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, payload, options);
    }

    /// <summary>Write distinct connection flows (unique aggregation) to CSV.</summary>
    public static void WriteCsv(string path, IEnumerable<ConnectionAggregate> flows)
    {
        EnsureDir(path);
        using var w = new StreamWriter(path, append: false, Encoding.UTF8);
        w.WriteLine("first_seen,last_seen,samples,source,protocol,direction,scope," +
                    "local_address,local_port,remote_address,remote_port,process_name,service_name");
        foreach (var f in flows)
        {
            w.Write(f.FirstSeen.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            w.Write(','); w.Write(f.LastSeen.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            w.Write(','); w.Write(f.SampleCount);
            w.Write(','); w.Write(Csv(f.Machine));
            w.Write(','); w.Write(f.Protocol);
            w.Write(','); w.Write(f.Direction);
            w.Write(','); w.Write(f.RemoteScope);
            w.Write(','); w.Write(Csv(f.LocalAddress));
            w.Write(','); w.Write(f.LocalPort);
            w.Write(','); w.Write(Csv(f.RemoteAddress));
            w.Write(','); w.Write(f.RemotePort);
            w.Write(','); w.Write(Csv(f.ProcessName));
            w.Write(','); w.Write(Csv(f.ServiceName));
            w.WriteLine();
        }
    }

    public static void WriteJson(string path, IEnumerable<ConnectionAggregate> flows)
    {
        EnsureDir(path);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var payload = flows.Select(f => new
        {
            f.FirstSeen,
            f.LastSeen,
            samples = f.SampleCount,
            source = f.Machine,
            protocol = f.Protocol.ToString(),
            direction = f.Direction.ToString(),
            scope = f.RemoteScope.ToString(),
            f.LocalAddress,
            f.LocalPort,
            f.RemoteAddress,
            f.RemotePort,
            f.ProcessName,
            f.ServiceName
        });
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, payload, options);
    }

    private static string Csv(string field)
    {
        if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    private static void EnsureDir(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
