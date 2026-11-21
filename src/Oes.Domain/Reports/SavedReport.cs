using Oes.Domain.Common;

namespace Oes.Domain.Reports;

/// <summary>Date ranges a saved report keeps rolling forward (or fixed dates).</summary>
public static class ReportRanges
{
    public const string ThisMonth = "this_month";
    public const string LastMonth = "last_month";
    public const string ThisYear = "this_year";
    public const string Last7 = "last_7";
    public const string Last30 = "last_30";
    public const string Last90 = "last_90";
    public const string Fixed = "fixed";

    public static readonly (string Key, string Label)[] All =
    [
        (ThisMonth, "This month"), (LastMonth, "Last month"), (ThisYear, "This year"),
        (Last7, "Last 7 days"), (Last30, "Last 30 days"), (Last90, "Last 90 days"), (Fixed, "These exact dates"),
    ];

    /// <summary>The dates a range covers on <paramref name="today"/>.</summary>
    public static (DateOnly From, DateOnly To) Resolve(string key, DateOnly today, DateOnly? from, DateOnly? to)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return key switch
        {
            ThisMonth => (monthStart, today),
            LastMonth => (monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            ThisYear => (new DateOnly(today.Year, 1, 1), today),
            Last7 => (today.AddDays(-6), today),
            Last30 => (today.AddDays(-29), today),
            Last90 => (today.AddDays(-89), today),
            _ => (from ?? today, to ?? today),
        };
    }
}

public static class ReportSchedules
{
    public const string None = "none";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
}

/// <summary>
/// A report someone saved with its filters, optionally emailed to them on a schedule. It always
/// runs as its owner, so it only ever shows what they're allowed to see.
/// </summary>
public class SavedReport : TenantEntity
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string RangeKey { get; set; } = ReportRanges.ThisMonth;
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public Guid? OwnerFilterId { get; set; }
    public string Schedule { get; set; } = ReportSchedules.None;
    /// <summary>Weekly: day of week (0 = Sunday). Monthly: day of month (1–28).</summary>
    public int ScheduleDay { get; set; }
    public DateTime? NextSendAt { get; set; }
    public DateTime? LastSentAt { get; set; }
    public string? LastError { get; set; }
}
