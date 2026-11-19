using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Booking;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Http;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Scheduling;

public sealed record BusyCalendarRow(BusyCalendar Calendar, DateTime? LastFetchedLocal);

/// <summary>
/// Outside calendars (.ics feed links) that block online booking. Everyone manages their own;
/// booking.manage can manage anyone's. Links are encrypted at rest and fetched through the SSRF-safe
/// fetcher; only busy times are kept (never titles), from yesterday to 90 days out, refreshed every
/// 15 minutes by the worker.
/// </summary>
public sealed class BusyCalendarService(OesDbContext db, RecordScope scope, ICurrentUser user, ISafeFetcher fetcher, IDataProtectionProvider protection,
    TenantTime time, IClock clock)
{
    public const int MaxPerUser = 5;
    public const int MaxBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);
    private IDataProtector Protector => protection.CreateProtector("Oes.BusyCalendar.Url");

    private async Task EnsureMayManageAsync(Guid userId)
    {
        if (userId != scope.Me && !await user.HasPermissionAsync(Permissions.BookingManage))
            throw new RecordForbiddenException("You can only change your own calendars.");
    }

    public async Task<List<BusyCalendarRow>> ForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await EnsureMayManageAsync(userId);
        var rows = new List<BusyCalendarRow>();
        foreach (var c in await db.BusyCalendars.Where(c => c.UserId == userId).OrderBy(c => c.Label).ToListAsync(ct))
            rows.Add(new BusyCalendarRow(c, c.LastFetchedAt is { } f ? await time.ToLocalAsync(f) : null));
        return rows;
    }

    /// <summary>Adds a feed and reads it straight away, so a bad link is caught while the person is here.</summary>
    public async Task<Guid> AddAsync(Guid userId, string? label, string url, CancellationToken ct = default)
    {
        await EnsureMayManageAsync(userId);
        if (!await db.Memberships.AnyAsync(m => m.UserId == userId && m.Status == MembershipStatus.Active, ct)) throw new CrmRuleException("Pick an active user.");
        if (await db.BusyCalendars.CountAsync(c => c.UserId == userId, ct) >= MaxPerUser) throw new CrmRuleException($"Up to {MaxPerUser} calendars each.");
        Uri uri;
        try { uri = SafeHttpFetcher.Normalize(url); }
        catch (SafeFetchException ex) { throw new CrmRuleException(ex.Message); }
        var cal = new BusyCalendar
        {
            UserId = userId, Label = ContactService.Clean(label) is { } l ? l[..Math.Min(l.Length, 100)] : uri.Host,
            UrlProtected = Protector.Protect(uri.ToString()), Host = uri.Host,
        };
        await RefreshAsync(cal, ct);
        if (cal.LastError is { } error) throw new CrmRuleException($"Couldn't read that calendar: {error}");
        db.BusyCalendars.Add(cal);
        await db.SaveChangesAsync(ct);
        return cal.Id;
    }

    public async Task RefreshNowAsync(Guid id, CancellationToken ct = default)
    {
        var cal = await db.BusyCalendars.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new RecordNotFoundException("Calendar");
        await EnsureMayManageAsync(cal.UserId);
        await RefreshAsync(cal, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetShowOnCalendarAsync(Guid id, bool show, CancellationToken ct = default)
    {
        var cal = await db.BusyCalendars.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new RecordNotFoundException("Calendar");
        await EnsureMayManageAsync(cal.UserId);
        cal.ShowOnCalendar = show;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var cal = await db.BusyCalendars.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new RecordNotFoundException("Calendar");
        await EnsureMayManageAsync(cal.UserId);
        db.BusyCalendars.Remove(cal); // its blocks go with it
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reads the feed and replaces its busy times. A failed read keeps the old times (better slightly
    /// stale than suddenly free) and records why. Saved with the caller's SaveChanges.
    /// </summary>
    internal async Task RefreshAsync(BusyCalendar cal, CancellationToken ct)
    {
        cal.LastFetchedAt = clock.UtcNow;
        string ics;
        try { ics = await fetcher.GetStringAsync(Protector.Unprotect(cal.UrlProtected), MaxBytes, ct); }
        catch (SafeFetchException ex) { cal.LastError = ex.Message; return; }
        catch (System.Security.Cryptography.CryptographicException) { cal.LastError = "The saved link can't be read any more. Remove it and add it again."; return; }
        if (!ics.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)) { cal.LastError = "That link isn't an .ics calendar feed."; return; }

        var now = clock.UtcNow;
        var busy = IcsParser.BusyTimes(ics, now.AddDays(-1), now.AddDays(90), await time.ZoneAsync());
        if (db.Entry(cal).State != EntityState.Detached)
            await db.BusyBlocks.Where(b => b.BusyCalendarId == cal.Id).ExecuteDeleteAsync(ct);
        foreach (var b in busy)
            db.BusyBlocks.Add(new BusyBlock { BusyCalendarId = cal.Id, UserId = cal.UserId, StartsAt = b.StartUtc, EndsAt = b.EndUtc });
        cal.BlockCount = busy.Count;
        cal.LastError = null;
    }

    /// <summary>Busy times to show on the calendar page (feeds marked to show; only people the viewer may see).</summary>
    public async Task<List<(Guid UserId, string Label, DateTime StartLocal, DateTime EndLocal)>> BlocksAsync(DateOnly from, DateOnly to, Guid? onlyUser,
        CancellationToken ct = default)
    {
        var (start, end) = await time.DayRangeUtcAsync(from, to);
        var me = scope.Me;
        var viewAll = await user.HasPermissionAsync(Permissions.CalendarViewAll, ct);
        var q = from b in db.BusyBlocks
                join c in db.BusyCalendars on b.BusyCalendarId equals c.Id
                where c.ShowOnCalendar && b.StartsAt < end && b.EndsAt > start
                select new { b.UserId, c.Label, b.StartsAt, b.EndsAt };
        if (!viewAll) q = q.Where(x => x.UserId == me);
        else if (onlyUser is { } u) q = q.Where(x => x.UserId == u);
        var rows = await q.OrderBy(x => x.StartsAt).Take(1000).ToListAsync(ct);
        var result = new List<(Guid, string, DateTime, DateTime)>();
        foreach (var r in rows) result.Add((r.UserId, r.Label, await time.ToLocalAsync(r.StartsAt), await time.ToLocalAsync(r.EndsAt)));
        return result;
    }
}

/// <summary>Worker: refreshes feeds not read in the last 15 minutes, per tenant, each in its own scope.</summary>
public static class BusyCalendarRunner
{
    public static async Task<int> RefreshAllAsync(IServiceScopeFactory scopes, CancellationToken ct = default)
    {
        List<Guid> tenants;
        await using (var s = scopes.CreateAsyncScope())
            tenants = await s.ServiceProvider.GetRequiredService<OesDbContext>().Tenants.Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync(ct);
        var refreshed = 0;
        foreach (var tenantId in tenants)
        {
            List<Guid> due;
            await using (var s = scopes.CreateAsyncScope())
            {
                s.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
                var cutoff = s.ServiceProvider.GetRequiredService<IClock>().UtcNow - BusyCalendarService.RefreshEvery + TimeSpan.FromMinutes(1);
                due = await s.ServiceProvider.GetRequiredService<OesDbContext>().BusyCalendars
                    .Where(c => c.LastFetchedAt == null || c.LastFetchedAt < cutoff).Select(c => c.Id).Take(500).ToListAsync(ct);
            }
            foreach (var id in due)
            {
                await using var s = scopes.CreateAsyncScope();
                s.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
                var db = s.ServiceProvider.GetRequiredService<OesDbContext>();
                try
                {
                    var cal = await db.BusyCalendars.FirstAsync(c => c.Id == id, ct);
                    await s.ServiceProvider.GetRequiredService<BusyCalendarService>().RefreshAsync(cal, ct);
                    await db.SaveChangesAsync(ct);
                    refreshed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    s.ServiceProvider.GetRequiredService<ILogger<BusyCalendarService>>().LogError(ex, "Refreshing busy calendar {CalendarId} failed", id);
                }
            }
        }
        return refreshed;
    }
}
