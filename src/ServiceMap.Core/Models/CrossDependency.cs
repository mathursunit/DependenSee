namespace ServiceMap.Core.Models;

/// <summary>
/// A dependency where one imported machine connects to another imported machine
/// — the flows that must stay reachable if the two are migrated separately.
/// </summary>
public sealed class CrossDependency
{
    public string FromMachine { get; set; } = string.Empty;
    public string ToMachine { get; set; } = string.Empty;
    public string FromWave { get; set; } = string.Empty;
    public string ToWave { get; set; } = string.Empty;

    /// <summary>True when the two machines are in different migration waves.</summary>
    public bool CrossesWaveBoundary =>
        !string.Equals(FromWave, ToWave, System.StringComparison.OrdinalIgnoreCase);
    public string Process { get; set; } = string.Empty;
    public Protocol Protocol { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public long SampleCount { get; set; }

    public string Endpoint =>
        RemotePort == 0 ? RemoteAddress : $"{RemoteAddress}:{RemotePort}";
}
