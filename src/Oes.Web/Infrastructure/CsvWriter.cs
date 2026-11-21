using System.Globalization;
using System.Text;

namespace Oes.Web.Infrastructure;

/// <summary>
/// RFC 4180 CSV for exports. Cells that a spreadsheet would run as a formula (=, +, -, @, tab, CR)
/// are prefixed with an apostrophe, so data typed by customers can't become a formula in Excel.
/// </summary>
public static class CsvWriter
{
    public static string Cell(object? value)
    {
        var s = value switch
        {
            null => "",
            decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        if (value is string && s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static void Row(StringBuilder sb, IEnumerable<object?> cells) => sb.Append(string.Join(',', cells.Select(Cell))).Append("\r\n");
}
