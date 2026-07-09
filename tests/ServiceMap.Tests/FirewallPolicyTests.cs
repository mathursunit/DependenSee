using ServiceMap.Firewall.Matching;
using Xunit;

namespace ServiceMap.Tests;

public class FirewallPolicyTests
{
    // A small but realistic policy: egress web allow, specific deny, catch-all
    // cleanup deny, an FQDN-ish unresolvable rule, and a Check Point on-prem rule.
    private const string Egress = """
        "","Name","Source Zone","Source Address","Destination Zone","Destination Address","Application","Service","Action","Rule Usage Rule Usage"
        "1","Allow Web Out","TRUST","10.10.0.0/16","UNTRUST","any","any","service-https","Allow","Used"
        "2","Allow Syslog","TRUST","10.10.0.0/16","UNTRUST","collector-10.99.0.5","any","service-udp-514","Allow","Used"
        "3","Block Telnet","TRUST","10.10.0.0/16","UNTRUST","any","any","service-tcp-23","Deny","Used"
        "4","SaaS via FQDN","TRUST","10.10.0.0/16","UNTRUST","saas.example.com","any","service-https","Allow","Unused"
        "5","Cleanup","any","any","any","any","any","any","Deny","Used"
        """;

    private const string Ingress = """
        "","Name","Source Zone","Source Address","Destination Zone","Destination Address","Application","Service","Action","Rule Usage Rule Usage"
        "1","Allow Admin RDP","MGMT","10.200.0.0/24","TRUST","10.10.5.20","any","service-tcp-3389","Allow","Used"
        """;

    private const string Checkpoint = """
        No.,Type,Name,Source,Destination,VPN,Services & Applications,Content,Action,Time,Track,Install On,Comments
        1,Rule,OnPrem SQL,app-10.10.5.0_24,sql01-10.60.1.5,Any,ms-sql,Any,Accept,Any,Log,Policy Targets,
        """;

    private static FirewallPolicy Policy() =>
        FirewallPolicy.Load(Egress, Ingress, Checkpoint, null);

    [Fact]
    public void OutboundAllowIsCovered()
    {
        var m = Policy().MatchFlow(new FlowKey("10.10.5.20", "8.8.8.8", 51000, 443, Outbound: true, "tcp"));
        Assert.Equal(FwCoverage.Covered, m.Coverage);
        Assert.Equal("Allow Web Out", m.RuleName);
        Assert.Equal("TRUST", m.SourceZone);
        Assert.Equal("UNTRUST", m.DestZone);
    }

    [Fact]
    public void ProtocolMismatchIsNotCovered()
    {
        // Rule allows UDP 514 to the collector; a TCP 514 flow must not match it.
        var udp = Policy().MatchFlow(new FlowKey("10.10.5.20", "10.99.0.5", 51000, 514, true, "udp"));
        var tcp = Policy().MatchFlow(new FlowKey("10.10.5.20", "10.99.0.5", 51000, 514, true, "tcp"));
        Assert.Equal(FwCoverage.Covered, udp.Coverage);
        Assert.Equal(FwCoverage.Gap, tcp.Coverage);
    }

    [Fact]
    public void SpecificDenyIsFlagged()
    {
        var m = Policy().MatchFlow(new FlowKey("10.10.5.20", "9.9.9.9", 51000, 23, true, "tcp"));
        Assert.Equal(FwCoverage.Denied, m.Coverage);
        Assert.Equal("Block Telnet", m.RuleName);
    }

    [Fact]
    public void CatchAllCleanupIsGapNotDenied()
    {
        // Port 9999 matches nothing specific; only the any/any cleanup would.
        var m = Policy().MatchFlow(new FlowKey("10.10.5.20", "9.9.9.9", 51000, 9999, true, "tcp"));
        Assert.Equal(FwCoverage.Gap, m.Coverage);
    }

    [Fact]
    public void InboundUsesIngressPolicy()
    {
        var m = Policy().MatchFlow(new FlowKey("10.10.5.20", "10.200.0.9", 3389, 52000, Outbound: false, "tcp"));
        Assert.Equal(FwCoverage.Covered, m.Coverage);
        Assert.Equal("Allow Admin RDP", m.RuleName);
    }

    [Fact]
    public void OnPremPolicyParticipates()
    {
        var m = Policy().MatchFlow(new FlowKey("10.10.5.20", "10.60.1.5", 51000, 1433, true, "tcp"));
        Assert.Equal(FwCoverage.Covered, m.Coverage);
        Assert.Equal("OnPrem SQL", m.RuleName);
    }

    [Fact]
    public void UnparseableIpsAreUnknown()
    {
        var m = Policy().MatchFlow(new FlowKey("", "fe80::1", 1, 2, true, "tcp"));
        Assert.Equal(FwCoverage.Unknown, m.Coverage);
    }

    [Fact]
    public void UnresolvableReferencesAreSurfaced()
    {
        var p = Policy();
        Assert.Contains("saas.example.com", p.UnresolvedReferences);
        Assert.DoesNotContain("10.10.0.0/16", p.UnresolvedReferences);
    }

    [Fact]
    public void AllowRulesCoveringFindsRulesForMachine()
    {
        var rules = Policy().AllowRulesCovering("10.10.5.20");
        var names = rules.Select(r => r.Name).ToList();
        Assert.Contains("Allow Web Out", names);       // source covers machine
        Assert.Contains("Allow Admin RDP", names);     // destination covers machine
        Assert.Contains("OnPrem SQL", names);          // on-prem source group covers machine
        Assert.DoesNotContain("Cleanup", names);       // catch-all excluded
        Assert.DoesNotContain("Block Telnet", names);  // deny excluded
    }

    [Fact]
    public void MultiFileLoadMergesRules()
    {
        var p = FirewallPolicy.Load(
            egressCsvs: new[] { Egress, Egress },      // two "regions"
            ingressCsvs: new[] { Ingress },
            checkpointCsvs: Array.Empty<string>(),
            groupsCsvs: Array.Empty<string>());
        Assert.Equal(10, p.Egress.Count);              // merged, headers not misparsed
        Assert.All(p.Egress, r => Assert.False(r.Name == "Name"));
    }
}
