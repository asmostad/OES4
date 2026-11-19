using System.Net;
using Oes.Infrastructure.Http;
using Oes.Infrastructure.Scheduling;

namespace Oes.UnitTests;

public class IcsParserTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private static readonly DateTime From = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string Cal(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("", events.Select(e => "BEGIN:VEVENT\r\n" + e.Replace("\n", "\r\n") + "\r\nEND:VEVENT\r\n")) + "END:VCALENDAR\r\n";

    private static List<BusyTime> Busy(params string[] events) => IcsParser.BusyTimes(Cal(events), From, To, Chicago);

    [Fact]
    public void Utc_zoned_floating_and_all_day_times()
    {
        var b = Busy(
            "DTSTART:20260305T150000Z\nDTEND:20260305T160000Z\nSUMMARY:Dentist",
            "DTSTART;TZID=America/New_York:20260306T090000\nDURATION:PT90M",
            "DTSTART:20260307T080000\nDTEND:20260307T083000",
            "DTSTART;VALUE=DATE:20260310\nDTEND;VALUE=DATE:20260312");
        Assert.Equal(new BusyTime(new(2026, 3, 5, 15, 0, 0), new(2026, 3, 5, 16, 0, 0)), b[0]);
        Assert.Equal(new BusyTime(new(2026, 3, 6, 14, 0, 0), new(2026, 3, 6, 15, 30, 0)), b[1]);  // 9 am EST
        Assert.Equal(new BusyTime(new(2026, 3, 7, 14, 0, 0), new(2026, 3, 7, 14, 30, 0)), b[2]);  // floating: company zone (CST)
        Assert.Equal(new BusyTime(new(2026, 3, 10, 5, 0, 0), new(2026, 3, 12, 5, 0, 0)), b[3]);   // two whole days (CDT after Mar 8)
    }

    [Fact]
    public void Free_cancelled_zero_length_and_broken_events_are_skipped()
    {
        Assert.Empty(Busy(
            "DTSTART:20260305T150000Z\nDTEND:20260305T160000Z\nTRANSP:TRANSPARENT",
            "DTSTART:20260305T150000Z\nDTEND:20260305T160000Z\nSTATUS:CANCELLED",
            "DTSTART:20260305T150000Z",
            "DTSTART:not-a-date\nDTEND:20260305T160000Z"));
    }

    [Fact]
    public void Folded_lines_and_quoted_parameters()
    {
        var b = Busy("DTSTART;TZID=\"America/Chicago\":2026030\n 9T100000\nDTEND;TZID=America/Chicago:20260309T110000");
        Assert.Equal(new BusyTime(new(2026, 3, 9, 15, 0, 0), new(2026, 3, 9, 16, 0, 0)), Assert.Single(b));
    }

    [Fact]
    public void Weekly_repeats_keep_the_wall_clock_across_daylight_saving_and_honor_exdate()
    {
        // Mondays and Wednesdays at 9 am Chicago, from Feb 2, with Mar 4 cancelled.
        var b = Busy("DTSTART;TZID=America/Chicago:20260202T090000\nDTEND;TZID=America/Chicago:20260202T100000\n" +
                     "RRULE:FREQ=WEEKLY;BYDAY=MO,WE\nEXDATE;TZID=America/Chicago:20260304T090000");
        Assert.Equal(8, b.Count); // March 2026: 5 Mondays and 4 Wednesdays, less the excluded one
        Assert.Equal(new DateTime(2026, 3, 2, 15, 0, 0), b[0].StartUtc); // CST
        Assert.Equal(new DateTime(2026, 3, 9, 14, 0, 0), b.Single(x => x.StartUtc.Day == 9).StartUtc); // CDT, still 9 am
        Assert.DoesNotContain(b, x => x.StartUtc.Day == 4);
    }

    [Fact]
    public void Daily_monthly_count_until_and_long_running_repeats()
    {
        Assert.Equal(3, Busy("DTSTART:20260301T120000Z\nDTEND:20260301T130000Z\nRRULE:FREQ=DAILY;COUNT=3").Count);
        Assert.Equal(5, Busy("DTSTART:20260301T120000Z\nDTEND:20260301T130000Z\nRRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20260309T235959Z").Count);
        Assert.Single(Busy("DTSTART:20250115T120000Z\nDTEND:20250115T130000Z\nRRULE:FREQ=MONTHLY")); // the 15th of March
        Assert.Equal(31, Busy("DTSTART:20050101T120000Z\nDTEND:20050101T130000Z\nRRULE:FREQ=DAILY").Count); // 21 years back: skips ahead
        Assert.Single(Busy("DTSTART;VALUE=DATE:20000320\nRRULE:FREQ=YEARLY")); // a birthday
    }
}

public class SafeFetcherTests
{
    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.20.0.1")]
    [InlineData("192.168.1.10")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void Private_and_special_addresses_are_refused(string ip) => Assert.False(SafeHttpFetcher.IsPublic(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("142.250.72.14")]
    [InlineData("2607:f8b0:4009:80b::200e")]
    public void Public_addresses_are_allowed(string ip) => Assert.True(SafeHttpFetcher.IsPublic(IPAddress.Parse(ip)));

    [Fact]
    public void Links_are_normalized_and_checked()
    {
        Assert.Equal("https://calendar.example.com/private/abc/basic.ics", SafeHttpFetcher.Normalize("webcal://calendar.example.com/private/abc/basic.ics").ToString());
        Assert.Throws<SafeFetchException>(() => SafeHttpFetcher.Normalize("http://calendar.example.com/a.ics"));
        Assert.Throws<SafeFetchException>(() => SafeHttpFetcher.Normalize("https://calendar.example.com:8443/a.ics"));
        Assert.Throws<SafeFetchException>(() => SafeHttpFetcher.Normalize("https://user:pw@calendar.example.com/a.ics"));
        Assert.Throws<SafeFetchException>(() => SafeHttpFetcher.Normalize("https://169.254.169.254/latest/meta-data"));
        Assert.Throws<SafeFetchException>(() => SafeHttpFetcher.Normalize("file:///c:/windows/win.ini"));
    }
}
