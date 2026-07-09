using ServiceMap.Firewall.Model;

namespace ServiceMap.Firewall.Parsing;

/// <summary>Parses a Palo Alto (Panorama) security-rule CSV export.</summary>
public static class PaloAltoRuleParser
{
    public static List<FwRule> Parse(string csvText, string policyName)
    {
        var rows = Csv.Parse(csvText);
        var rules = new List<FwRule>();
        if (rows.Count == 0) return rules;
        var h = Csv.Header(rows[0]);
        var order = 0;
        for (var r = 1; r < rows.Count; r++)
        {
            var name = Csv.Col(rows[r], h, "Name");
            if (name.Length == 0) continue;
            var rule = new FwRule
            {
                Vendor = FwVendor.PaloAlto,
                Policy = policyName,
                Order = ++order,
                Name = name,
                Action = ToAction(Csv.Col(rows[r], h, "Action")),
                SourceZone = Csv.Col(rows[r], h, "Source Zone"),
                DestZone = Csv.Col(rows[r], h, "Destination Zone"),
                Usage = Csv.Col(rows[r], h, "Rule Usage Rule Usage")
            };
            rule.Sources.AddRange(Csv.Multi(Csv.Col(rows[r], h, "Source Address")));
            rule.Destinations.AddRange(Csv.Multi(Csv.Col(rows[r], h, "Destination Address")));
            rule.Services.AddRange(Csv.Multi(Csv.Col(rows[r], h, "Service")));
            rule.Applications.AddRange(Csv.Multi(Csv.Col(rows[r], h, "Application")));
            rules.Add(rule);
        }
        return rules;
    }

    private static FwAction ToAction(string a) => a.Trim().ToLowerInvariant() switch
    {
        "allow" => FwAction.Allow,
        "deny" or "drop" or "reset-client" or "reset-server" or "reset-both" => FwAction.Deny,
        _ => FwAction.Other
    };
}
