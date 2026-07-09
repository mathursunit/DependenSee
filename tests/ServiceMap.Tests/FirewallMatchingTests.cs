using ServiceMap.Firewall.Matching;
using ServiceMap.Firewall.Model;
using Xunit;

namespace ServiceMap.Tests;

public class IpUtilTests
{
    [Theory]
    [InlineData("10.94.9.167", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("256.1.1.1", false)]
    [InlineData("1.2.3", false)]
    [InlineData("a.b.c.d", false)]
    public void ParsesDottedQuads(string s, bool ok) =>
        Assert.Equal(ok, IpUtil.TryParse(s, out _));

    [Fact]
    public void RoundTrips()
    {
        Assert.True(IpUtil.TryParse("10.181.44.7", out var ip));
        Assert.Equal("10.181.44.7", IpUtil.ToStr(ip));
    }
}

public class CidrTests
{
    private static uint Ip(string s) { IpUtil.TryParse(s, out var v); return v; }

    [Fact]
    public void ContainsRespectsPrefix()
    {
        var net = new Cidr(Ip("10.181.44.0"), 22);
        Assert.True(net.Contains(Ip("10.181.44.1")));
        Assert.True(net.Contains(Ip("10.181.47.255")));
        Assert.False(net.Contains(Ip("10.181.48.0")));
    }

    [Fact]
    public void Slash32MatchesOnlyItself()
    {
        var host = new Cidr(Ip("10.94.9.167"), 32);
        Assert.True(host.Contains(Ip("10.94.9.167")));
        Assert.False(host.Contains(Ip("10.94.9.168")));
    }

    [Fact]
    public void PrefixZeroMatchesEverything()
    {
        var all = new Cidr(Ip("1.2.3.4"), 0);
        Assert.True(all.Contains(Ip("8.8.8.8")));
        Assert.True(all.Contains(Ip("192.168.0.1")));
    }

    [Fact]
    public void NetworkIsMaskedOnConstruction() =>
        Assert.Equal("10.181.44.0/22", new Cidr(Ip("10.181.45.77"), 22).ToString());
}

public class AddressExtractorTests
{
    [Theory]
    [InlineData("10.0.0.0/24", "10.0.0.0/24")]                        // bare CIDR
    [InlineData("cmusswhadpdc01-10.94.9.167", "10.94.9.167/32")]      // name-embedded host
    [InlineData("Name-10.181.44.0_22", "10.181.44.0/22")]             // name-embedded network
    public void ExtractsNetworks(string reference, string expected)
    {
        Assert.True(AddressExtractor.TryExtract(reference, out var cidr));
        Assert.Equal(expected, cidr.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("any")]
    [InlineData("dns-service")]
    public void NonAddressReferencesFail(string reference) =>
        Assert.False(AddressExtractor.TryExtract(reference, out _));
}

public class PolicyIndexTests
{
    private static uint Ip(string s) { IpUtil.TryParse(s, out var v); return v; }

    private static FwGroup Group(string name, params string[] members)
    {
        var g = new FwGroup { Name = name };
        g.Members.AddRange(members);
        return g;
    }

    [Fact]
    public void EnrichFindsMostSpecificObjectAndGroups()
    {
        var idx = new PolicyIndex(new[]
        {
            Group("DataCenter", "Name-10.181.44.0_22"),
            Group("DomainControllers", "cmusswhadpdc01-10.94.9.167")
        });

        var hit = idx.Enrich(Ip("10.94.9.167"));
        Assert.True(hit.Found);
        Assert.Equal("cmusswhadpdc01", hit.ObjectName);
        Assert.Contains("DomainControllers", hit.Groups);
    }

    [Fact]
    public void EnrichMissReturnsNotFound() =>
        Assert.False(new PolicyIndex(new[] { Group("G", "10.0.0.0/24") })
            .Enrich(Ip("172.20.1.1")).Found);

    [Fact]
    public void ResolveFlattensNestedGroups()
    {
        var idx = new PolicyIndex(new[]
        {
            Group("Parent", "Child", "10.1.0.0/16"),
            Group("Child", "10.2.0.0/24")
        });

        var nets = idx.Resolve("Parent");
        Assert.Equal(2, nets.Count);
        Assert.Contains(nets, c => c.ToString() == "10.1.0.0/16");
        Assert.Contains(nets, c => c.ToString() == "10.2.0.0/24");
    }

    [Fact]
    public void ResolveSurvivesGroupCycles()
    {
        var idx = new PolicyIndex(new[]
        {
            Group("A", "B", "10.1.0.0/24"),
            Group("B", "A", "10.2.0.0/24")
        });

        var nets = idx.Resolve("A");   // must terminate
        Assert.Equal(2, nets.Count);
    }

    [Fact]
    public void RuleReferencesAreIndexedForEnrichment()
    {
        var rule = new FwRule();
        rule.Sources.Add("apphost01-10.50.1.20");
        var idx = new PolicyIndex(Array.Empty<FwGroup>(), new[] { rule });

        var hit = idx.Enrich(Ip("10.50.1.20"));
        Assert.Equal("apphost01", hit.ObjectName);
    }
}
