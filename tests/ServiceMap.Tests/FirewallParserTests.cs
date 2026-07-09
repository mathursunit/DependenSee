using ServiceMap.Firewall.Model;
using ServiceMap.Firewall.Parsing;
using Xunit;

namespace ServiceMap.Tests;

public class PaloAltoRuleParserTests
{
    // Header shape taken from a real Panorama export (sanitized values).
    private const string Csv = """
        "","Name","Location","Tags","Type","Source Zone","Source Address","Source Device","Destination Zone","Destination Address","Destination Device","Application","Service","URL Category","Action","Profile","Options","Target","Rule Usage Rule Usage","Rule Usage Apps Seen","Days With No New Apps","Modified","Created"
        "1","Allow Web","LOC","none","universal","TRUST","10.10.0.0/16","any","UNTRUST","any","any","any","service-https","any","Allow","p","o","t","Used","5","0","m","c"
        "2","Block Legacy","LOC","none","universal","TRUST","10.10.0.0/16;10.20.0.0/16","any","UNTRUST","legacy-host-10.9.9.9","any","any","service-tcp-23","any","Deny","p","o","t","Unused","0","90","m","c"
        """;

    [Fact]
    public void ParsesRulesWithZonesAndUsage()
    {
        var rules = PaloAltoRuleParser.Parse(Csv, "Test");
        Assert.Equal(2, rules.Count);

        var allow = rules[0];
        Assert.Equal("Allow Web", allow.Name);
        Assert.Equal(FwAction.Allow, allow.Action);
        Assert.Equal("TRUST", allow.SourceZone);
        Assert.Equal("UNTRUST", allow.DestZone);
        Assert.Equal("Used", allow.Usage);
        Assert.Single(allow.Sources);
        Assert.Contains("service-https", allow.Services);

        var deny = rules[1];
        Assert.Equal(FwAction.Deny, deny.Action);
        Assert.Equal(2, deny.Sources.Count);      // ';'-separated multi-value
        Assert.Equal("Unused", deny.Usage);
    }
}

public class CheckpointRuleParserTests
{
    private const string Csv = """
        No.,Type,Name,Source,Destination,VPN,Services & Applications,Content,Action,Time,Track,Install On,Comments
        1,Rule,Evilnets blacklist,Any,evilnets,Any,Any,Any,Drop,Any,Log,Policy Targets,RITM01
        2,Rule,DC access,corp-10.50.0.0_16,dc01-10.50.1.10,Any,https;ldap,Any,Accept,Any,Log,Policy Targets,
        """;

    [Fact]
    public void ParsesRulesAndActions()
    {
        var rules = CheckpointRuleParser.Parse(Csv);
        Assert.Equal(2, rules.Count);
        Assert.Equal(FwAction.Deny, rules[0].Action);
        Assert.Equal(FwAction.Allow, rules[1].Action);
        Assert.Equal(2, rules[1].Services.Count);   // ';'-separated
        Assert.Equal(FwVendor.CheckPoint, rules[1].Vendor);
    }
}

public class AddressGroupParserTests
{
    private const string Csv = """
        "Name","Location","Members Count","Addresses","Tags"
        "DC-Servers","AMER","2","dc01-10.50.1.10;dc02-10.50.1.11",""
        """;

    [Fact]
    public void ParsesGroupsAndMembers()
    {
        var groups = AddressGroupParser.Parse(Csv);
        var g = Assert.Single(groups);
        Assert.Equal("DC-Servers", g.Name);
        Assert.Equal(2, g.Members.Count);
    }
}
