using ServiceMap.Platform.Abstractions;
using ServiceMap.Platform.Linux;

// Headless validation of the Linux port target: exercises the same abstractions
// the collector uses, printing a sample of connections and services.
IPlatformProvider platform = new LinuxPlatformProvider();

Console.WriteLine($"Platform : {platform.PlatformName}");
Console.WriteLine($"Elevated : {platform.IsElevated}");
Console.WriteLine();

var samples = platform.ConnectionSampler.Sample();
Console.WriteLine($"Connections sampled: {samples.Count}");
Console.WriteLine($"{"PROTO",-5} {"DIR",-9} {"STATE",-12} {"LOCAL",-24} {"REMOTE",-24} {"PROC"}");
foreach (var s in samples.Take(25))
{
    Console.WriteLine($"{s.Protocol,-5} {s.Direction,-9} {s.State,-12} " +
                      $"{s.LocalEndpoint,-24} {(string.IsNullOrEmpty(s.RemoteEndpoint) ? "-" : s.RemoteEndpoint),-24} " +
                      $"{s.ProcessName}({s.ProcessId})");
}

Console.WriteLine();
var services = platform.ServiceEnumerator.GetServices();
Console.WriteLine($"Services found: {services.Count}");
foreach (var svc in services.Take(15))
    Console.WriteLine($"  {svc.Name,-40} {svc.State}");
