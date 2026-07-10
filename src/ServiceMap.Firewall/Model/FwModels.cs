namespace ServiceMap.Firewall.Model;

/// <summary>Which firewall a policy came from.</summary>
public enum FwVendor { PaloAlto, CheckPoint }

/// <summary>Rule disposition, normalised across vendors.</summary>
public enum FwAction { Allow, Deny, Other }

/// <summary>An address group: a named set of member references (objects, nested groups, or literal CIDRs).</summary>
public sealed class FwGroup
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public List<string> Members { get; } = new();
}

/// <summary>A normalised firewall rule from any vendor.</summary>
public sealed class FwRule
{
    public FwVendor Vendor { get; set; }
    public string Policy { get; set; } = string.Empty;   // e.g. "CWAN Egress", "Checkpoint"
    public int Order { get; set; }

    /// <summary>
    /// The export's own rule number: Palo Alto's position within the device
    /// group (first CSV column), Check Point's "No." column. This is how a
    /// firewall admin finds the rule in Panorama/SmartConsole.
    /// </summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Palo Alto device group ("Location" column); empty for Check Point.</summary>
    public string Location { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable unique reference, e.g. "AMER-AWS-DYN-EGRESS #12".</summary>
    public string RuleRef =>
        (Location.Length > 0 ? Location + " " : string.Empty) +
        (Number.Length > 0 ? "#" + Number : string.Empty);
    public FwAction Action { get; set; }

    public List<string> Sources { get; } = new();        // refs: object/group names, CIDRs, "any"
    public List<string> Destinations { get; } = new();
    public List<string> Services { get; } = new();        // service objects, e.g. "service-https", "any"
    public List<string> Applications { get; } = new();    // app-ids, e.g. "dns", "web-browsing"

    public string SourceZone { get; set; } = string.Empty;
    public string DestZone { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;     // Palo Alto "Rule Usage" (Used/Unused/...)
    public string Comment { get; set; } = string.Empty;

    public bool IsAllow => Action == FwAction.Allow;
}
