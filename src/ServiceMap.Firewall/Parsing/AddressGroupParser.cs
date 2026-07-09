using ServiceMap.Firewall.Model;

namespace ServiceMap.Firewall.Parsing;

/// <summary>Parses a Palo Alto address-group export (Name, Location, Members Count, Addresses, Tags).</summary>
public static class AddressGroupParser
{
    public static List<FwGroup> Parse(string csvText)
    {
        var rows = Csv.Parse(csvText);
        var groups = new List<FwGroup>();
        if (rows.Count == 0) return groups;
        var h = Csv.Header(rows[0]);
        for (var r = 1; r < rows.Count; r++)
        {
            var name = Csv.Col(rows[r], h, "Name");
            if (name.Length == 0) continue;
            var g = new FwGroup { Name = name, Location = Csv.Col(rows[r], h, "Location") };
            g.Members.AddRange(Csv.Multi(Csv.Col(rows[r], h, "Addresses")));
            groups.Add(g);
        }
        return groups;
    }
}
