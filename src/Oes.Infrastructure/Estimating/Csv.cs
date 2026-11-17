using System.Text;

namespace Oes.Infrastructure.Estimating;

/// <summary>Small RFC 4180 CSV reader (quoted fields, doubled quotes, embedded commas and newlines).</summary>
public static class Csv
{
    public static IEnumerable<List<string>> Read(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        int c;
        while ((c = reader.Read()) != -1)
        {
            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { field.Append('"'); reader.Read(); }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"' when field.Length == 0: inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    if (row.Count > 1 || row[0].Length > 0) yield return row;
                    row = [];
                    break;
                default: field.Append(ch); break;
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Count > 1 || row[0].Length > 0) yield return row;
        }
    }
}
