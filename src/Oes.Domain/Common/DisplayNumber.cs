namespace Oes.Domain.Common;

/// <summary>
/// Formats human-readable record numbers: prefix, year, then a per-tenant counter that
/// restarts each year, e.g. P-2026-0311. Decided 2026-11-16 for projects, estimates
/// and work orders.
/// </summary>
public static class DisplayNumber
{
    public const int MinDigits = 4;

    public static string Format(string prefix, int year, long value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return $"{prefix}-{year:D4}-{value.ToString().PadLeft(MinDigits, '0')}";
    }
}

/// <summary>The numbered record kinds and their prefixes.</summary>
public static class NumberedKinds
{
    public static readonly (string Key, string Prefix) Project = ("project", "P");
    public static readonly (string Key, string Prefix) Estimate = ("estimate", "EST");
    public static readonly (string Key, string Prefix) WorkOrder = ("work_order", "WO");
    public static readonly (string Key, string Prefix) Opportunity = ("opportunity", "OPP");
}
