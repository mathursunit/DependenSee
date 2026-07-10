using ServiceMap.Core.Models;
using ServiceMap.Reporting;
using Xunit;

namespace ServiceMap.Tests;

public class CloudRuleGeneratorTests
{
    private static DossierData Sample()
    {
        var d = new DossierData { MachineName = "APP SRV.01" };
        d.Inbound.Add(new ConnectionAggregate
        {
            Direction = ConnectionDirection.Inbound, Protocol = Protocol.Tcp,
            LocalPort = 443, RemoteAddress = "10.2.0.99", ServiceName = "IIS", SampleCount = 100
        });
        d.Inbound.Add(new ConnectionAggregate
        {
            Direction = ConnectionDirection.Inbound, Protocol = Protocol.Tcp,
            LocalPort = 443, RemoteAddress = "10.2.0.98", ServiceName = "IIS", SampleCount = 50
        });
        d.Outbound.Add(new ConnectionAggregate
        {
            Direction = ConnectionDirection.Outbound, Protocol = Protocol.Tcp,
            RemotePort = 1433, RemoteAddress = "10.60.1.5", ProcessName = "w3wp", SampleCount = 90
        });
        return d;
    }

    [Fact]
    public void GeneratesAllThreeFiles()
    {
        var files = CloudRuleGenerator.Generate(Sample());
        Assert.Contains("aws-security-group.tf", files.Keys);
        Assert.Contains("azure-nsg.tf", files.Keys);
        Assert.Contains("cloud-rules.json", files.Keys);
    }

    [Fact]
    public void AwsIngressGroupsPeersPerPort()
    {
        var tf = CloudRuleGenerator.Generate(Sample())["aws-security-group.tf"];
        Assert.Contains("from_port   = 443", tf);
        Assert.Contains("\"10.2.0.99/32\"", tf);
        Assert.Contains("\"10.2.0.98/32\"", tf);
        Assert.Contains("from_port   = 1433", tf);            // egress
        Assert.Contains("resource \"aws_security_group\" \"app-srv-01\"", tf);   // sanitized name
        Assert.Contains("REVIEW BEFORE APPLYING", tf);
    }

    [Fact]
    public void AzureRulesHaveIncrementingPriorities()
    {
        var tf = CloudRuleGenerator.Generate(Sample())["azure-nsg.tf"];
        Assert.Contains("priority                   = 100", tf);
        Assert.Contains("priority                   = 110", tf);
        Assert.Contains("direction                  = \"Inbound\"", tf);
        Assert.Contains("direction                  = \"Outbound\"", tf);
    }

    [Fact]
    public void JsonIsNeutralAndParsable()
    {
        var json = CloudRuleGenerator.Generate(Sample())["cloud-rules.json"];
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("ingress").GetArrayLength());
        Assert.Equal(1, doc.RootElement.GetProperty("egress").GetArrayLength());
    }
}
