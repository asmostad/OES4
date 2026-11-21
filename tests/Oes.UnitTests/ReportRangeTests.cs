using Oes.Domain.Reports;

namespace Oes.UnitTests;

public class ReportRangeTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    [Theory]
    [InlineData(ReportRanges.ThisMonth, "2026-03-01", "2026-03-10")]
    [InlineData(ReportRanges.LastMonth, "2026-02-01", "2026-02-28")]
    [InlineData(ReportRanges.ThisYear, "2026-01-01", "2026-03-10")]
    [InlineData(ReportRanges.Last7, "2026-03-04", "2026-03-10")]
    [InlineData(ReportRanges.Last30, "2026-02-09", "2026-03-10")]
    [InlineData(ReportRanges.Last90, "2025-12-11", "2026-03-10")]
    public void Rolling_ranges_end_today_and_last_month_is_whole(string key, string from, string to) =>
        Assert.Equal((DateOnly.Parse(from), DateOnly.Parse(to)), ReportRanges.Resolve(key, Today, null, null));

    [Fact]
    public void Fixed_dates_stay_put() =>
        Assert.Equal((new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30)),
            ReportRanges.Resolve(ReportRanges.Fixed, Today, new DateOnly(2025, 1, 1), new DateOnly(2025, 6, 30)));
}
