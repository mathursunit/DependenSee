using ServiceMap.Firewall.Matching;
using Xunit;

namespace ServiceMap.Tests;

public class ServicePortsTests
{
    [Theory]
    [InlineData("service-https", 443, "tcp", true)]
    [InlineData("service-https", 443, "udp", false)]   // protocol-aware
    [InlineData("service-https", 80, "tcp", false)]
    [InlineData("ntp", 123, "udp", true)]
    [InlineData("ntp", 123, "tcp", false)]
    [InlineData("dns", 53, "tcp", true)]               // "any"-protocol service
    [InlineData("dns", 53, "udp", true)]
    public void KnownServicesRespectProtocol(string name, int port, string proto, bool expected) =>
        Assert.Equal(expected, ServicePorts.Matches(name, port, proto));

    [Theory]
    [InlineData("tcp-8009", 8009, "tcp", true)]
    [InlineData("tcp-8009", 8009, "udp", false)]
    [InlineData("service-udp-514", 514, "udp", true)]
    [InlineData("service-udp-514", 514, "tcp", false)]
    [InlineData("service-tcp_9443", 9443, "tcp", true)]
    [InlineData("tcp-8000-8010", 8005, "tcp", true)]    // range
    [InlineData("tcp-8000-8010", 8011, "tcp", false)]
    [InlineData("service-8080", 8080, "tcp", true)]     // bare port: protocol unspecified
    [InlineData("service-8080", 8080, "udp", true)]
    public void PortInNamePatterns(string name, int port, string proto, bool expected) =>
        Assert.Equal(expected, ServicePorts.Matches(name, port, proto));

    [Theory]
    [InlineData("SNMPv3", 3)]        // digits inside a word must NOT become a port
    [InlineData("ldap-2000-app", 2000)]
    [InlineData("my-service", 80)]
    public void ArbitraryNamesWithDigitsMatchNothing(string name, int port)
    {
        Assert.False(ServicePorts.Matches(name, port, "tcp"));
        Assert.Empty(ServicePorts.PortsFor(name));
    }

    [Fact]
    public void PortsForExpandsRanges() =>
        Assert.Equal(new[] { 8000, 8001, 8002 }, ServicePorts.PortsFor("tcp-8000-8002"));
}
