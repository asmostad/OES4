using System.Text;
using System.Text.RegularExpressions;

namespace Oes.Domain.Crm;

/// <summary>
/// Builds a comparable key for US addresses so "123 North Main Street Apt 4" and
/// "123 n. main st #4" are recognized as the same property. Not a full USPS standardizer;
/// geocoding/validation can replace it later behind the same key.
/// </summary>
public static partial class AddressKey
{
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["STREET"] = "ST", ["STR"] = "ST", ["AVENUE"] = "AVE", ["AV"] = "AVE", ["ROAD"] = "RD", ["DRIVE"] = "DR",
        ["LANE"] = "LN", ["COURT"] = "CT", ["BOULEVARD"] = "BLVD", ["CIRCLE"] = "CIR", ["PLACE"] = "PL",
        ["TERRACE"] = "TER", ["PARKWAY"] = "PKWY", ["HIGHWAY"] = "HWY", ["TRAIL"] = "TRL", ["SQUARE"] = "SQ",
        ["HOLLOW"] = "HOLW", ["CROSSING"] = "XING", ["POINT"] = "PT", ["RIDGE"] = "RDG", ["MOUNT"] = "MT",
        ["NORTH"] = "N", ["SOUTH"] = "S", ["EAST"] = "E", ["WEST"] = "W",
        ["NORTHEAST"] = "NE", ["NORTHWEST"] = "NW", ["SOUTHEAST"] = "SE", ["SOUTHWEST"] = "SW",
        ["APARTMENT"] = "#", ["APT"] = "#", ["UNIT"] = "#", ["SUITE"] = "#", ["STE"] = "#", ["NO"] = "#",
        ["BUILDING"] = "BLDG", ["FLOOR"] = "FL",
    };

    [GeneratedRegex(@"[^A-Z0-9# ]")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static string NormalizeLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "";
        var s = line.ToUpperInvariant().Replace("#", " # ");
        s = NonWord().Replace(s, " ");
        var parts = Spaces().Split(s.Trim()).Where(p => p.Length > 0).Select(p => Words.GetValueOrDefault(p, p));
        var joined = string.Join(' ', parts);
        return joined.Replace("# #", "#").Replace("# ", "#");
    }

    public static string Zip5(string? postalCode)
    {
        var digits = new string((postalCode ?? "").Where(char.IsDigit).ToArray());
        return digits.Length >= 5 ? digits[..5] : digits;
    }

    /// <summary>LINE1 LINE2|ZIP5, or LINE1 LINE2|CITY|STATE when there is no ZIP.</summary>
    public static string For(string line1, string? line2, string? city, string? state, string? postalCode)
    {
        var street = NormalizeLine($"{line1} {line2}");
        var zip = Zip5(postalCode);
        return zip.Length == 5
            ? $"{street}|{zip}"
            : $"{street}|{NormalizeLine(city)}|{NormalizeLine(state)}";
    }
}

public static class ContactValue
{
    /// <summary>Emails lower-cased; phones to E.164 (US default); websites lower-cased without scheme.</summary>
    public static string Normalize(string kind, string value)
    {
        value = value.Trim();
        if (kind == ContactMethodKinds.Email) return value.ToLowerInvariant();
        if (ContactMethodKinds.IsPhone(kind)) return Phone(value);
        if (kind == ContactMethodKinds.Website)
            return value.ToLowerInvariant().Replace("https://", "").Replace("http://", "").TrimEnd('/');
        return value;
    }

    public static string Phone(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value) if (char.IsDigit(c)) sb.Append(c);
        var d = sb.ToString();
        if (d.Length == 10) return "+1" + d;
        if (d.Length == 11 && d[0] == '1') return "+" + d;
        return d.Length > 0 && value.TrimStart().StartsWith('+') ? "+" + d : d;
    }

    /// <summary>(402) 555-0134 for US numbers; anything else is shown as entered.</summary>
    public static string FormatPhone(string normalized, string fallback) =>
        normalized.Length == 12 && normalized.StartsWith("+1")
            ? $"({normalized[2..5]}) {normalized[5..8]}-{normalized[8..]}"
            : fallback;
}
