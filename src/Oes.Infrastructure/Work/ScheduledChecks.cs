using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Booking;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Subcontractors;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Infrastructure.Work;

public sealed record CheckResult(int Alerts, int Reminders);

/// <summary>
/// Time-based alerts, run per company every 15 minutes by the worker. Every alert has a dedupe key,
/// so running more often (or twice) never sends one twice; a key includes the date it's about, so
/// moving a due date or a stage change can alert again. Only recent events alert, so turning this on
/// doesn't flood anyone with years of old overdue tasks.
/// </summary>
public sealed class ScheduledChecks(
    OesDbContext db, TenantContext tenant, IClock clock, TenantTime time, NotificationService notifications, ISystemMailer mailer, ILogger<ScheduledChecks> log)
{
    public static readonly TimeSpan DueSoonWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(2);
    public const int InsuranceWarningDays = 30;
    public const int EstimateWarningDays = 3;

    public static async Task<CheckResult> RunAllAsync(IServiceScopeFactory scopes, CancellationToken ct = default)
    {
        List<Guid> tenants;
        await using (var s = scopes.CreateAsyncScope())
            tenants = await s.ServiceProvider.GetRequiredService<OesDbContext>().Tenants
                .Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync(ct);
        int alerts = 0, reminders = 0;
        foreach (var id in tenants)
        {
            await using var s = scopes.CreateAsyncScope();
            try
            {
                var r = await s.ServiceProvider.GetRequiredService<ScheduledChecks>().RunAsync(id, ct);
                alerts += r.Alerts; reminders += r.Reminders;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                s.ServiceProvider.GetRequiredService<ILogger<ScheduledChecks>>().LogError(ex, "Scheduled checks failed for tenant {TenantId}", id);
            }
        }
        return new CheckResult(alerts, reminders);
    }

    public async Task<CheckResult> RunAsync(Guid tenantId, CancellationToken ct = default)
    {
        tenant.SetTenant(tenantId);
        var alerts = 0;
        alerts += await TasksAsync(ct);
        alerts += await StaleOpportunitiesAsync(ct);
        alerts += await EstimatesAsync(ct);
        alerts += await SubInsuranceAsync(ct);
        await db.SaveChangesAsync(ct);
        var reminders = await BookingRemindersAsync(ct);
        return new CheckResult(alerts, reminders);
    }

    private static string Stamp(DateTime d) => d.ToString("yyyyMMddHHmm");

    private async Task<int> TasksAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var today = await time.TodayAsync();
        var (todayStart, _) = await time.DayRangeUtcAsync(today, today);
        var n = 0;

        var dueSoon = await db.Tasks.Where(t => t.CompletedAt == null && t.HasDueTime && t.DueAt > now && t.DueAt <= now + DueSoonWindow).ToListAsync(ct);
        foreach (var t in dueSoon)
            if (await notifications.NotifyOnceAsync(t.AssignedToUserId, $"task.due:{t.Id}:{Stamp(t.DueAt!.Value)}", "task.due",
                    $"Due at {(await time.ToLocalAsync(t.DueAt.Value)):h:mm tt}: {t.Title}", null, RecordUrls.For(t.EntityType, t.EntityId) ?? "/Tasks", ct)) n++;

        // Became overdue recently: timed tasks past their time, date-only tasks once their day is over.
        var overdue = await db.Tasks.Where(t => t.CompletedAt == null && t.DueAt != null && t.DueAt > now - Lookback
                                                && (t.HasDueTime ? t.DueAt <= now : t.DueAt < todayStart)).ToListAsync(ct);
        foreach (var t in overdue)
            if (await notifications.NotifyOnceAsync(t.AssignedToUserId, $"task.overdue:{t.Id}:{Stamp(t.DueAt!.Value)}", "task.overdue",
                    $"Overdue: {t.Title}", null, RecordUrls.For(t.EntityType, t.EntityId) ?? "/Tasks", ct)) n++;
        return n;
    }

    private async Task<int> StaleOpportunitiesAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var rows = await (from o in db.Opportunities
                          where o.Outcome == Outcomes.Open && o.ArchivedAt == null && o.OwnerUserId != null
                          join s in db.PipelineStages on o.StageId equals s.Id
                          where s.StaleAfterHours != null
                          select new { o.Id, o.Number, o.Name, o.OwnerUserId, Since = o.StageChangedAt ?? o.CreatedAt, Hours = s.StaleAfterHours!.Value, Stage = s.Name })
            .ToListAsync(ct);
        var n = 0;
        foreach (var o in rows)
        {
            var staleAt = o.Since.AddHours(o.Hours);
            if (staleAt > now || staleAt < now - Lookback) continue;
            if (await notifications.NotifyOnceAsync(o.OwnerUserId, $"opp.stale:{o.Id}:{Stamp(o.Since)}", "opportunity.stale",
                    $"{o.Number} has been in {o.Stage} too long", o.Name, $"/Opportunities/View/{o.Id}", ct)) n++;
        }
        return n;
    }

    private async Task<int> EstimatesAsync(CancellationToken ct)
    {
        var today = await time.TodayAsync();
        var rows = await db.Estimates.Where(e => e.Status == EstimateStatuses.Issued && e.ExpiresOn != null && e.OwnerUserId != null
                                                 && e.ExpiresOn >= today.AddDays(-2) && e.ExpiresOn <= today.AddDays(EstimateWarningDays))
            .Select(e => new { e.Id, e.Number, e.OwnerUserId, e.ExpiresOn }).ToListAsync(ct);
        var n = 0;
        foreach (var e in rows)
        {
            var expired = e.ExpiresOn < today;
            var (key, title) = expired
                ? ($"est.expired:{e.Id}:{e.ExpiresOn:yyyyMMdd}", $"{e.Number} expired on {e.ExpiresOn:MMM d}")
                : ($"est.expiring:{e.Id}:{e.ExpiresOn:yyyyMMdd}", $"{e.Number} expires {(e.ExpiresOn == today ? "today" : $"on {e.ExpiresOn:MMM d}")}");
            if (await notifications.NotifyOnceAsync(e.OwnerUserId, key, expired ? "estimate.expired" : "estimate.expiring", title,
                    "Follow up before the price changes.", $"/Estimates/View/{e.Id}", ct)) n++;
        }
        return n;
    }

    private async Task<int> SubInsuranceAsync(CancellationToken ct)
    {
        var today = await time.TodayAsync();
        var subs = await (from s in db.Subcontractors
                          where s.Status == SubStatuses.Active && s.InsuranceExpiresOn != null && s.InsuranceExpiresOn <= today.AddDays(InsuranceWarningDays)
                          join c in db.Contacts on s.ContactId equals c.Id
                          select new { s.Id, Name = c.DisplayName, s.InsuranceExpiresOn }).ToListAsync(ct);
        if (subs.Count == 0) return 0;
        var managers = await notifications.UsersWithPermissionAsync(Permissions.SubcontractorsManage, ct);
        var n = 0;
        foreach (var s in subs)
        {
            var expired = s.InsuranceExpiresOn < today;
            var key = $"sub.insurance:{s.Id}:{s.InsuranceExpiresOn:yyyyMMdd}:{(expired ? "expired" : "soon")}";
            var title = expired ? $"{s.Name}: insurance expired {s.InsuranceExpiresOn:MMM d}" : $"{s.Name}: insurance expires {s.InsuranceExpiresOn:MMM d}";
            foreach (var m in managers)
                if (await notifications.NotifyOnceAsync(m, key, "subcontractor.insurance", title, "Ask for a new certificate.", $"/Subcontractors/View/{s.Id}", ct)) n++;
        }
        return n;
    }

    /// <summary>Emails people who booked online a reminder the day before (not if they booked within the last day).</summary>
    private async Task<int> BookingRemindersAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.Bookings.Where(b => b.Status == BookingStatuses.Booked && b.ReminderSentAt == null
                                               && b.StartsAt > now && b.StartsAt <= now.AddHours(24) && b.CreatedAt < b.StartsAt.AddHours(-24))
            .ToListAsync(ct);
        if (due.Count == 0) return 0;
        var company = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.Name).FirstAsync(ct);
        var types = await db.BookingTypes.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var n = 0;
        foreach (var b in due)
        {
            var host = await db.Users.Where(u => u.Id == b.HostUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct) ?? company;
            var when = (await time.ToLocalAsync(b.StartsAt)).ToString("dddd, MMMM d 'at' h:mm tt");
            static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
            var html = $"<p>Hi {E(b.FirstName)},</p><p>A reminder: your {E(types.GetValueOrDefault(b.BookingTypeId, "appointment"))} with {E(company)} is " +
                       $"<strong>{E(when)}</strong>{(b.Line1 is null ? "" : $" at {E(b.Line1)}, {E(b.City)}")}. {E(host)} will see you then.</p>" +
                       "<p>Need to change it? Use the link in your confirmation email, or reply to let us know.</p>";
            b.ReminderSentAt = now; // set first: never send twice, even if the mailer fails
            await db.SaveChangesAsync(ct);
            try { await mailer.SendAsync(new SystemEmail(b.Email, $"Reminder: your appointment with {company}", html), ct); n++; }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Booking reminder {BookingId} failed", b.Id); }
        }
        return n;
    }
}

/// <summary>Nightly housekeeping across all companies.</summary>
public sealed class NightlyCleanup(OesDbContext db, IClock clock)
{
    public static readonly TimeSpan KeepReadNotifications = TimeSpan.FromDays(90);
    public static readonly TimeSpan KeepUnreadNotifications = TimeSpan.FromDays(180);
    public static readonly TimeSpan KeepDeadLinks = TimeSpan.FromDays(30);

    public sealed record Result(int Notifications, int Links);

    public async Task<Result> RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var all = new[] { OesDbContext.TenantFilter };
        // Alerts keep their dedupe rows (a deleted one could fire again), but only within the look-back
        // window, which is far shorter than these retention periods.
        var notifications = await db.Notifications.IgnoreQueryFilters(all)
            .Where(n => (n.ReadAt != null && n.ReadAt < now - KeepReadNotifications) || (n.ReadAt == null && n.CreatedAt < now - KeepUnreadNotifications))
            .ExecuteDeleteAsync(ct);
        // Public links long past their expiry (or revoked) can't be used again; their hashes aren't worth keeping.
        var links = await db.ExternalAccessTokens.IgnoreQueryFilters(all)
            .Where(t => t.ExpiresAt < now - KeepDeadLinks || (t.RevokedAt != null && t.RevokedAt < now - KeepDeadLinks))
            .ExecuteDeleteAsync(ct);
        return new Result(notifications, links);
    }
}
