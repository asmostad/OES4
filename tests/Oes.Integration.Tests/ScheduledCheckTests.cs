using Microsoft.EntityFrameworkCore;
using Oes.Domain.Booking;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Work;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Subcontractors;
using Oes.Infrastructure.Work;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class ScheduledCheckTests(MySqlFixture fx)
{
    // Tuesday Dec 1, 2026, 9:00 am in Chicago.
    private static readonly DateTime T = new(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 12, 1);

    private async Task<(Guid Tenant, AppUser Admin, AppUser Rep)> WorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        return (t.Id, admin, rep);
    }

    private async Task RunAsync(Guid tenant, DateTime at)
    {
        fx.Clock.Pinned = at;
        await using (fx.ScopeFor(null)) { }
        await using var s = fx.ScopeFor(null);
        await s.Get<ScheduledChecks>().RunAsync(tenant);
    }

    private async Task<List<Notification>> AlertsAsync(Guid tenant, AppUser user)
    {
        await using var s = fx.ScopeAs(tenant, user);
        return await s.Get<OesDbContext>().Notifications.Where(n => n.UserId == user.Id && n.DedupeKey != null).OrderBy(n => n.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task Tasks_alert_when_due_soon_and_when_overdue_once_and_only_for_recent_ones()
    {
        var (tenant, admin, rep) = await WorldAsync();
        try
        {
            fx.Clock.Pinned = T;
            await using (var s = fx.ScopeAs(tenant, rep))
            {
                var work = s.Get<WorkService>();
                await work.CreateTaskAsync(new TaskInput { Title = "Call at 9:30", DueDate = Today, DueTime = new TimeOnly(9, 30) });
                await work.CreateTaskAsync(new TaskInput { Title = "Yesterday's", DueDate = Today.AddDays(-1) });
                await work.CreateTaskAsync(new TaskInput { Title = "Ancient", DueDate = Today.AddDays(-20) });
                await work.CreateTaskAsync(new TaskInput { Title = "Next week", DueDate = Today.AddDays(7) });
            }
            await RunAsync(tenant, T);
            await RunAsync(tenant, T); // no repeats
            var alerts = await AlertsAsync(tenant, rep);
            Assert.Equal(["task.due", "task.overdue"], alerts.Select(a => a.Kind).Order().ToList());
            Assert.Contains(alerts, a => a.Title == "Due at 9:30 AM: Call at 9:30");
            Assert.Contains(alerts, a => a.Title == "Overdue: Yesterday's");

            await RunAsync(tenant, T.AddMinutes(45)); // 9:45: the 9:30 task is now overdue too
            Assert.Contains(await AlertsAsync(tenant, rep), a => a.Title == "Overdue: Call at 9:30");
            Assert.Equal(3, (await AlertsAsync(tenant, rep)).Count);
        }
        finally { fx.Clock.Pinned = null; }
    }

    [Fact]
    public async Task Stale_opportunities_expiring_estimates_and_sub_insurance_alert_the_right_people()
    {
        var (tenant, admin, rep) = await WorldAsync();
        try
        {
            fx.Clock.Pinned = T;
            Guid fresh, old, est, sub;
            await using (var s = fx.ScopeAs(tenant, admin))
            {
                var db = s.Get<OesDbContext>();
                var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Stale", LastName = "Lead", OwnerUserId = rep.Id });
                fresh = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, OwnerUserId = rep.Id });
                old = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, OwnerUserId = rep.Id });
                var stage = await db.Opportunities.Where(o => o.Id == fresh).Select(o => o.StageId).SingleAsync();
                await db.PipelineStages.Where(p => p.Id == stage).ExecuteUpdateAsync(u => u.SetProperty(p => p.StaleAfterHours, 24));
                await db.Opportunities.Where(o => o.Id == fresh).ExecuteUpdateAsync(u => u.SetProperty(o => o.StageChangedAt, T.AddHours(-25)));
                await db.Opportunities.Where(o => o.Id == old).ExecuteUpdateAsync(u => u.SetProperty(o => o.StageChangedAt, T.AddDays(-30)));

                est = await s.Get<EstimateService>().CreateAsync(new NewEstimateInput { OpportunityId = fresh, Method = EstimateMethods.LineItem });
                await db.Estimates.Where(e => e.Id == est).ExecuteUpdateAsync(u => u.SetProperty(e => e.Status, EstimateStatuses.Issued)
                    .SetProperty(e => e.ExpiresOn, Today.AddDays(2)).SetProperty(e => e.OwnerUserId, rep.Id));

                sub = await s.Get<SubcontractorService>().CreateAsync(new NewSubInput { OrganizationName = "Lapsing Roofers", TradeCodes = ["roofing"] });
                await db.Subcontractors.Where(x => x.Id == sub).ExecuteUpdateAsync(u => u.SetProperty(x => x.InsuranceExpiresOn, Today.AddDays(14)));
            }
            await RunAsync(tenant, T);
            await RunAsync(tenant, T.AddMinutes(15));

            var repAlerts = await AlertsAsync(tenant, rep);
            Assert.Single(repAlerts, a => a.Kind == "opportunity.stale" && a.Url == $"/Opportunities/View/{fresh}"); // the 30-day-old one stays quiet
            Assert.Single(repAlerts, a => a.Kind == "estimate.expiring" && a.Url == $"/Estimates/View/{est}");
            Assert.DoesNotContain(repAlerts, a => a.Kind == "subcontractor.insurance"); // reps don't manage subs
            var adminAlerts = await AlertsAsync(tenant, admin);
            Assert.Single(adminAlerts, a => a.Kind == "subcontractor.insurance" && a.Title == "Lapsing Roofers: insurance expires Dec 15");

            // The day after the estimate's expiry date, a second (different) alert.
            await RunAsync(tenant, T.AddDays(3));
            Assert.Single(await AlertsAsync(tenant, rep), a => a.Kind == "estimate.expired" && a.Title.EndsWith("expired on Dec 3"));
            // Once the insurance lapses, another.
            await RunAsync(tenant, T.AddDays(15));
            Assert.Contains(await AlertsAsync(tenant, admin), a => a.Title == "Lapsing Roofers: insurance expired Dec 15");
        }
        finally { fx.Clock.Pinned = null; }
    }

    [Fact]
    public async Task Online_bookings_get_one_reminder_email_the_day_before()
    {
        var (tenant, admin, _) = await WorldAsync();
        var email = $"remind-{Guid.NewGuid():N}@example.com";
        var lastMinute = $"late-{Guid.NewGuid():N}@example.com";
        try
        {
            await using (var s = fx.ScopeAs(tenant, admin))
            {
                var db = s.Get<OesDbContext>();
                void Book(string to, DateTime createdAt)
                {
                    fx.Clock.Pinned = createdAt;
                    var appt = new Appointment { Title = "Inspection", StartsAt = T.AddHours(20), EndsAt = T.AddHours(21), AssignedToUserId = admin.Id };
                    db.Appointments.Add(appt);
                    db.Bookings.Add(new Booking
                    {
                        BookingTypeId = Guid.NewGuid(), HostUserId = admin.Id, AppointmentId = appt.Id, StartsAt = appt.StartsAt, EndsAt = appt.EndsAt,
                        FirstName = "Rhea", LastName = "Minder", Email = to, Phone = "+14025550111", Line1 = "1 Remind Rd", City = "Lincoln",
                    });
                }
                Book(email, T.AddDays(-3));
                await db.SaveChangesAsync();
                Book(lastMinute, T.AddHours(-1)); // booked inside the last day: the confirmation is enough
                await db.SaveChangesAsync();
            }
            await RunAsync(tenant, T);
            await RunAsync(tenant, T.AddMinutes(15));

            var mails = Directory.GetFiles(Path.Combine(fx.FilesRoot, "mail"), "*.html").Select(File.ReadAllText).ToList();
            Assert.Single(mails, m => m.Contains(email));
            Assert.DoesNotContain(mails, m => m.Contains(lastMinute));
            Assert.Contains("Wednesday, December 2 at 5:00 AM", mails.Single(m => m.Contains(email)));
        }
        finally { fx.Clock.Pinned = null; }
    }

    [Fact]
    public async Task Nightly_cleanup_removes_old_notifications_and_dead_links()
    {
        var (tenant, admin, rep) = await WorldAsync();
        try
        {
            await using (var s = fx.ScopeAs(tenant, admin))
            {
                var db = s.Get<OesDbContext>();
                fx.Clock.Pinned = T.AddDays(-120);
                db.Notifications.Add(new Notification { UserId = rep.Id, Kind = "old", Title = "Old and read", ReadAt = T.AddDays(-110) });
                db.ExternalAccessTokens.Add(new ExternalAccessToken { Purpose = TokenPurposes.SubPortal, EntityType = "x", EntityId = Guid.NewGuid(), TokenHash = Guid.NewGuid().ToString("N"), ExpiresAt = T.AddDays(-60) });
                await db.SaveChangesAsync();
                fx.Clock.Pinned = T.AddDays(-10);
                db.Notifications.Add(new Notification { UserId = rep.Id, Kind = "recent", Title = "Recent and read", ReadAt = T.AddDays(-9) });
                await db.SaveChangesAsync();
            }
            fx.Clock.Pinned = T;
            await using (var s = fx.ScopeFor(null)) await s.Get<NightlyCleanup>().RunAsync();
            await using (var s = fx.ScopeAs(tenant, admin))
            {
                var db = s.Get<OesDbContext>();
                Assert.Equal(["recent"], await db.Notifications.Where(n => n.UserId == rep.Id).Select(n => n.Kind).ToListAsync());
                Assert.False(await db.ExternalAccessTokens.AnyAsync(t => t.EntityType == "x"));
            }
        }
        finally { fx.Clock.Pinned = null; }
    }
}
