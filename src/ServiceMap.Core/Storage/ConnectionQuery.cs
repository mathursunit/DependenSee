using ServiceMap.Core.Models;

namespace ServiceMap.Core.Storage;

/// <summary>Filter criteria for querying stored connection samples.</summary>
public sealed class ConnectionQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Case-insensitive substring match on process name.</summary>
    public string? ProcessName { get; set; }

    /// <summary>Match a specific local port.</summary>
    public int? LocalPort { get; set; }

    /// <summary>Case-insensitive substring match on remote address.</summary>
    public string? RemoteAddress { get; set; }

    /// <summary>Exclude rows whose process/service name contains this substring.</summary>
    public string? ProcessNotContains { get; set; }

    /// <summary>Exclude rows whose remote address contains this substring.</summary>
    public string? RemoteNotContains { get; set; }

    /// <summary>Hide connections whose service-side port is ephemeral (dynamic range).</summary>
    public bool ExcludeEphemeral { get; set; }

    /// <summary>Ephemeral/dynamic port threshold (IANA default 49152).</summary>
    public int EphemeralThreshold { get; set; } = 49152;

    public Protocol? Protocol { get; set; }
    public ConnectionDirection? Direction { get; set; }

    /// <summary>Restrict to IPv4 or IPv6 (based on the local address).</summary>
    public AddressFamilyOption AddressFamily { get; set; } = AddressFamilyOption.Any;

    /// <summary>Restrict by remote-address scope (private vs. internet, etc.).</summary>
    public IpScope? Scope { get; set; }

    /// <summary>Maximum rows to return (newest first). Defaults to 5000.</summary>
    public int Limit { get; set; } = 5000;
}
