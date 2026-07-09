using ServiceMap.Firewall.Model;

namespace ServiceMap.Firewall.Parsing;

/// <summary>Parses a Check Point rule CSV export (No., Name, Source, Destination, Services &amp; Applications, Action, ...).</summary>
public static class CheckpointRuleParser
{
    public static List<FwRule> Parse(string csvText, string policyName = "Checkpoint")
    {
        var rows = Csv.Parse(csvText);
        var rules = new List<FwRule>();
        if (rows.Count == 0) return rules;
        var h = Csv.Header(rows[0]);
        var order = 0;
        for (var r = 1; r < rows.Count; r++)
        {
            var type = Csv.Col(rows[r], h, "Type");
            // Skip section headers / non-rule rows.
            if (!type.Equals("Rule", StringComparison.OrdinalIgnoreCase) &&
                !h.ContainsKey("Type")) { /* fall through when no Type column */ }
            var name = Csv.Col(rows[r], h, "Name");
            var src = Csv.Col(rows[r], h, "Source");
            var dst = Csv.Col(rows[r], h, "Destination");
            if (src.Length == 0 && dst.Length == 0) continue;

            var rule = new FwRule
            {
                Vendor = FwVendor.CheckPoint,
                Policy = policyName,
                Order = ++order,
                Name = name.Length > 0 ? name : $"rule-{order}",
                Action = ToAction(Csv.Col(rows[r], h, "Action")),
                Comment = Csv.Col(rows[r], h, "Comments")
            };
            rule.Sources.AddRange(SplitList(src));
            rule.Destinations.AddRange(SplitList(dst));
            rule.Services.AddRange(SplitList(Csv.Col(rows[r], h, "Services & Applications")));
            rules.Add(rule);
        }
        return rules;
    }

    // Check Point cells separate multiple objects by ';' or newlines.
    private static IEnumerable<string> SplitList(string cell) =>
        cell.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static FwAction ToAction(string a) => a.Trim().ToLowerInvariant() switch
    {
        "accept" or "allow" => FwAction.Allow,
        "drop" or "reject" or "deny" => FwAction.Deny,
        _ => FwAction.Other
    };
}
