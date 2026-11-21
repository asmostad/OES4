using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Work;

/// <summary>
/// Converts between UTC (how everything is stored) and the tenant's time zone (how people
/// think about due dates and appointments). IANA zone names work on Windows via ICU.
/// </summary>
public sealed class TenantTime(OesDbContext db, ITenantContext tenant, IClock clock)
{
    private TimeZoneInfo? _zone;

    public async Task<TimeZoneInfo> ZoneAsync()
    {
        if (_zone is not null) return _zone;
        var id = tenant.TenantId is { } t ? await db.Tenants.Where(x => x.Id == t).Select(x => x.TimeZone).FirstOrDefaultAsync() : null;
        try { _zone = TimeZoneInfo.FindSystemTimeZoneById(id ?? "America/Chicago"); }
        catch (TimeZoneNotFoundException) { _zone = TimeZoneInfo.Utc; }
        return _zone;
    }

    public async Task<DateTime> ToLocalAsync(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), await ZoneAsync());

    public async Task<DateTime> ToUtcAsync(DateOnly date, TimeOnly time)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        var zone = await ZoneAsync();
        // A time skipped by daylight saving moves forward an hour instead of failing.
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public async Task<DateOnly> TodayAsync() => DateOnly.FromDateTime(await ToLocalAsync(clock.UtcNow));

    /// <summary>UTC range covering whole local days [from, to].</summary>
    public async Task<(DateTime Start, DateTime End)> DayRangeUtcAsync(DateOnly from, DateOnly to) =>
        (await ToUtcAsync(from, TimeOnly.MinValue), await ToUtcAsync(to.AddDays(1), TimeOnly.MinValue));
}
