using ServiceMap.Core.Models;
using ServiceMap.Core.Net;
using Xunit;

namespace ServiceMap.Tests;

public class IpClassifierTests
{
    [Theory]
    // RFC1918
    [InlineData("10.0.0.1", IpScope.Private)]
    [InlineData("10.255.255.254", IpScope.Private)]
    [InlineData("172.16.0.1", IpScope.Private)]
    [InlineData("172.31.255.1", IpScope.Private)]
    [InlineData("192.168.1.1", IpScope.Private)]
    // Boundaries just outside RFC1918
    [InlineData("172.15.0.1", IpScope.Public)]
    [InlineData("172.32.0.1", IpScope.Public)]
    [InlineData("192.169.0.1", IpScope.Public)]
    [InlineData("11.0.0.1", IpScope.Public)]
    // CGNAT 100.64/10
    [InlineData("100.64.0.1", IpScope.Private)]
    [InlineData("100.127.255.1", IpScope.Private)]
    [InlineData("100.63.0.1", IpScope.Public)]
    [InlineData("100.128.0.1", IpScope.Public)]
    // Loopback / link-local / unspecified
    [InlineData("127.0.0.1", IpScope.Loopback)]
    [InlineData("169.254.10.20", IpScope.LinkLocal)]
    [InlineData("0.0.0.0", IpScope.None)]
    // Public
    [InlineData("8.8.8.8", IpScope.Public)]
    public void ClassifiesIPv4(string addr, IpScope expected) =>
        Assert.Equal(expected, IpClassifier.Classify(addr));

    [Theory]
    [InlineData("::1", IpScope.Loopback)]
    [InlineData("fe80::1", IpScope.LinkLocal)]
    [InlineData("fc00::1", IpScope.Private)]
    [InlineData("fd12:3456::1", IpScope.Private)]
    [InlineData("2001:4860:4860::8888", IpScope.Public)]
    [InlineData("::", IpScope.None)]
    public void ClassifiesIPv6(string addr, IpScope expected) =>
        Assert.Equal(expected, IpClassifier.Classify(addr));

    [Fact]
    public void V4MappedV6IsClassifiedAsV4()
    {
        Assert.Equal(IpScope.Private, IpClassifier.Classify("::ffff:192.168.1.5"));
        Assert.Equal(IpScope.Public, IpClassifier.Classify("::ffff:8.8.8.8"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-ip")]
    [InlineData("999.1.1.1")]
    public void UnparseableReturnsNone(string? addr) =>
        Assert.Equal(IpScope.None, IpClassifier.Classify(addr));

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("::1", false)]
    [InlineData("::ffff:1.2.3.4", false)]   // stored form is v6
    [InlineData("", false)]
    public void IsIPv4Detects(string addr, bool expected) =>
        Assert.Equal(expected, IpClassifier.IsIPv4(addr));
}
