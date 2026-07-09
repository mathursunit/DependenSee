namespace ServiceMap.Firewall.Parsing;

/// <summary>Minimal RFC-4180 CSV reader: handles quoted fields, escaped quotes, embedded commas/newlines.</summary>
public static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var field = new System.Text.StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;
        // Strip a UTF-8 BOM if present.
        int i = 0;
        if (text.Length > 0 && text[0] == '﻿') i = 1;

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); break;
                    case '\r': break;
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        rows.Add(row.ToArray()); row = new List<string>();
                        break;
                    default: field.Append(c); break;
                }
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }

    /// <summary>Map a header row to column indexes (case-insensitive, trimmed).</summary>
    public static Dictionary<string, int> Header(string[] headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerRow.Length; i++)
            map[headerRow[i].Trim()] = i;
        return map;
    }

    public static string Col(string[] row, Dictionary<string, int> h, string name) =>
        h.TryGetValue(name, out var i) && i < row.Length ? row[i].Trim() : string.Empty;

    /// <summary>Split a multi-value cell (Palo Alto uses ';').</summary>
    public static List<string> Multi(string cell) =>
        cell.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
