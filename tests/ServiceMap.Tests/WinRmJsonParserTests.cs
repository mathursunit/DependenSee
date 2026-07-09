using ServiceMap.Core.Models;
using ServiceMap.Remote.Parsing;
using Xunit;

namespace ServiceMap.Tests;

public class WinRmJsonParserTests
{
    private static readonly DateTime Ts = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string Json = """
        {
          "host": "APPSRV01",
          "procs": [
            { "Pid": 4321, "Name": "w3wp", "Path": "C:\\Windows\\System32\\inetsrv\\w3wp.exe" },
            { "Pid": 5555, "Name": "sqlservr", "Path": "" }
          ],
          "services": [
            { "Name": "W3SVC", "DisplayName": "World Wide Web Publishing Service",
              "State": "Running", "StartMode": "Auto", "Pid": 4321,
              "Path": "C:\\Windows\\system32\\svchost.exe -k iissvcs", "Account": "LocalSystem" },
            { "Name": "MSSQLSERVER", "DisplayName": "SQL Server (MSSQLSERVER)",
              "State": "Running", "StartMode": "Auto", "Pid": 5555, "Path": "", "Account": "" }
          ],
          "tcp": [
            { "LocalAddress": "0.0.0.0", "LocalPort": 443, "RemoteAddress": "0.0.0.0",
              "RemotePort": 0, "State": "Listen", "Pid": 4321 },
            { "LocalAddress": "10.2.0.4", "LocalPort": 443, "RemoteAddress": "10.2.0.99",
              "RemotePort": 61022, "State": "Established", "Pid": 4321 },
            { "LocalAddress": "10.2.0.4", "LocalPort": 59211, "RemoteAddress": "10.2.0.50",
              "RemotePort": 1433, "State": "Established", "Pid": 5555 }
          ],
          "udp": [
            { "LocalAddress": "0.0.0.0", "LocalPort": 161, "Pid": 4321 }
          ]
        }
        """;

    [Fact]
    public void ParsesHostAndCounts()
    {
        var parsed = WinRmJsonParser.Parse(Json, Ts);
        Assert.Equal("APPSRV01", parsed.MachineName);
        Assert.Equal(2, parsed.Services.Count);
        Assert.Equal(4, parsed.Connections.Count);
    }

    [Fact]
    public void AttributesProcessAndService()
    {
        var parsed = WinRmJsonParser.Parse(Json, Ts);
        var inbound = parsed.Connections.Single(c => c.RemotePort == 61022);
        Assert.Equal("w3wp", inbound.ProcessName);
        Assert.Equal(@"C:\Windows\System32\inetsrv\w3wp.exe", inbound.ProcessPath);
        Assert.Equal("World Wide Web Publishing Service", inbound.ServiceName);
    }

    [Fact]
    public void AssignsDirections()
    {
        var parsed = WinRmJsonParser.Parse(Json, Ts);
        Assert.Equal(ConnectionDirection.Listen,
            parsed.Connections.Single(c => c.State == TcpState.Listen && c.Protocol == Protocol.Tcp).Direction);
        Assert.Equal(ConnectionDirection.Inbound,
            parsed.Connections.Single(c => c.RemotePort == 61022).Direction);
        Assert.Equal(ConnectionDirection.Outbound,
            parsed.Connections.Single(c => c.RemotePort == 1433).Direction);
        Assert.Equal(ConnectionDirection.Listen,
            parsed.Connections.Single(c => c.Protocol == Protocol.Udp).Direction);
    }

    [Fact]
    public void ListenerRemoteEndpointIsCleared()
    {
        var parsed = WinRmJsonParser.Parse(Json, Ts);
        var listener = parsed.Connections.Single(c => c.State == TcpState.Listen && c.Protocol == Protocol.Tcp);
        Assert.Equal(string.Empty, listener.RemoteAddress);
        Assert.Equal(0, listener.RemotePort);
    }

    [Fact]
    public void ServiceRecordsCarryDetails()
    {
        var parsed = WinRmJsonParser.Parse(Json, Ts);
        var w3 = parsed.Services.Single(s => s.Name == "W3SVC");
        Assert.Equal("Running", w3.State);
        Assert.Equal("Auto", w3.StartMode);
        Assert.Equal("LocalSystem", w3.Account);
        Assert.Equal(4321, w3.ProcessId);
    }
}
