using Microsoft.EntityFrameworkCore;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class WorkTests(MySqlFixture fx)
{
    private sealed record World(Guid Tenant, AppUser Admin, AppUser Rep, Guid Property, Guid Opportunity);

    private async Task<World> WorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        await using var s = fx.ScopeAs(t.Id, admin);
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "1 Agenda Ave", City = "Lincoln", State = "NE", PostalCode = "68502" });
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Agenda", LastName = "Owner", OwnerUserId = rep.Id });
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property, OwnerUserId = rep.Id });
        return new World(t.Id, admin, rep, property, opp);
    }

    [Fact]
    public async Task Assigning_a_task_notifies_the_assignee_but_never_yourself_and_it_rolls_up()
    {
        var w = await WorldAsync();
        Guid fromAdmin;
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            await s.Get<WorkService>().CreateTaskAsync(new TaskInput { Title = "Call back", EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity });
            Assert.Equal(0, await s.Get<NotificationService>().UnreadCountAsync()); // own task: no ping
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            fromAdmin = await s.Get<WorkService>().CreateTaskAsync(new TaskInput
            {
                Title = "Order materials", AssignedToUserId = w.Rep.Id, Priority = "high", DueDate = new DateOnly(2026, 12, 1),
                EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity,
            });
            // Tasks on the opportunity show on the property above it.
            var onProperty = await s.Get<WorkService>().TasksAsync(onlyMine: false, includeDone: false, EntityTypes.Property, w.Property);
            Assert.Equal(2, onProperty.Count);
            Assert.All(onProperty, t => Assert.Equal($"/Opportunities/View/{w.Opportunity}", t.RecordUrl));
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            var n = s.Get<NotificationService>();
            Assert.Equal(1, await n.UnreadCountAsync());
            var note = Assert.Single(await n.LatestAsync());
            Assert.Equal("task.assigned", note.Kind);
            Assert.Equal($"/Opportunities/View/{w.Opportunity}", await n.OpenAsync(note.Id));
            Assert.Equal(0, await n.UnreadCountAsync());

            var mine = await s.Get<WorkService>().TasksAsync(onlyMine: true, includeDone: false);
            Assert.Equal(2, mine.Count);
            await s.Get<WorkService>().SetDoneAsync(fromAdmin, true);
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            Assert.Contains(await s.Get<NotificationService>().LatestAsync(), x => x.Kind == "task.completed");
            var timeline = await s.Get<RecordItems>().TimelineAsync(EntityTypes.Opportunity, w.Opportunity, TimelineScope.Everything, ItemTypes.Activity);
            Assert.Contains(timeline, i => i.Text.Contains("Order materials"));
        }
    }

    [Fact]
    public async Task Reps_only_see_and_touch_their_own_tasks_and_records()
    {
        var w = await WorldAsync();
        var other = await fx.AddMemberAsync(w.Tenant, "Sales Rep");
        Guid task;
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
            task = await s.Get<WorkService>().CreateTaskAsync(new TaskInput { Title = "Private follow-up", EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity });

        await using (var s = fx.ScopeAs(w.Tenant, other))
        {
            var work = s.Get<WorkService>();
            Assert.Empty(await work.TasksAsync(onlyMine: false, includeDone: true));
            await Assert.ThrowsAsync<RecordNotFoundException>(() => work.SetDoneAsync(task, true));
            await Assert.ThrowsAsync<RecordNotFoundException>(() => work.CreateTaskAsync(new TaskInput { Title = "Snoop", EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity }));
            await Assert.ThrowsAsync<RecordNotFoundException>(() => work.TasksAsync(false, false, EntityTypes.Opportunity, w.Opportunity));
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin)) // tasks.view_all
            Assert.Single(await s.Get<WorkService>().TasksAsync(onlyMine: false, includeDone: false));
    }

    [Fact]
    public async Task Due_dates_and_appointments_use_the_company_time_zone()
    {
        var w = await WorldAsync(); // tenants default to America/Chicago
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var work = s.Get<WorkService>();
        var db = s.Get<OesDbContext>();

        var dateOnly = await work.CreateTaskAsync(new TaskInput { Title = "Date only", DueDate = new DateOnly(2026, 12, 1) });
        var timed = await work.CreateTaskAsync(new TaskInput { Title = "Timed", DueDate = new DateOnly(2026, 7, 1), DueTime = new TimeOnly(8, 30) });
        Assert.Equal(new DateTime(2026, 12, 1, 18, 0, 0), (await db.Tasks.SingleAsync(t => t.Id == dateOnly)).DueAt); // noon CST
        Assert.Equal(new DateTime(2026, 7, 1, 13, 30, 0), (await db.Tasks.SingleAsync(t => t.Id == timed)).DueAt); // 8:30 CDT

        var appt = await work.CreateAppointmentAsync(new AppointmentInput
        {
            Title = "Adjuster meeting", TypeKey = "adjuster_meeting", Date = new DateOnly(2026, 7, 1), StartTime = new TimeOnly(9, 0), DurationMinutes = 90,
            EntityType = EntityTypes.Opportunity, EntityId = w.Opportunity, AssignedToUserId = w.Rep.Id,
        });
        var stored = await db.Appointments.SingleAsync(a => a.Id == appt);
        Assert.Equal(new DateTime(2026, 7, 1, 14, 0, 0), stored.StartsAt);
        Assert.Equal(new DateTime(2026, 7, 1, 15, 30, 0), stored.EndsAt);
        Assert.Contains("1 Agenda Ave", stored.Location); // defaults to the property address

        var day = await work.AppointmentsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1), null);
        var row = Assert.Single(day);
        Assert.Equal(new DateTime(2026, 7, 1, 9, 0, 0), row.StartLocal);
        Assert.Empty(await work.AppointmentsAsync(new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 8), null));
        Assert.Single(await work.AppointmentsAsync(new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 1), null, EntityTypes.Property, w.Property));

        await Assert.ThrowsAsync<CrmRuleException>(() => work.CreateAppointmentAsync(new AppointmentInput { Title = "Too long", Date = new DateOnly(2026, 7, 1), DurationMinutes = 2000 }));
    }

    [Fact]
    public async Task Price_exceptions_ping_the_other_approvers()
    {
        var w = await WorldAsync();
        var manager = await fx.AddMemberAsync(w.Tenant, "Sales Manager");
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var est = s.Get<EstimateService>();
            var id = await est.CreateAsync(new NewEstimateInput { OpportunityId = w.Opportunity, Method = EstimateMethods.LineItem });
            var structure = (await s.Get<PropertyService>().GetAsync(w.Property)).Structures.Single().Id;
            var roofing = (await s.Get<MeasurementService>().TradesAsync()).Single(x => x.Code == "roofing");
            await est.AddSectionAsync(id, "Roof", structure, roofing.Id);
            var section = (await est.GetAsync(id)).Sections.Single(x => x.Section.Name == "Roof").Section.Id;
            await est.AddMiscLineAsync(id, section, "Satellite dish detach & reset", 1, "EA", 125m, false);
            Assert.Equal(0, await s.Get<NotificationService>().UnreadCountAsync()); // the requester isn't pinged
        }
        await using (var s = fx.ScopeAs(w.Tenant, manager))
        {
            var note = Assert.Single(await s.Get<NotificationService>().LatestAsync());
            Assert.Equal("pricing.approval_needed", note.Kind);
            Assert.Equal("/Estimates/Approvals", note.Url);
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep)) // no pricing.approve
            Assert.Equal(0, await s.Get<NotificationService>().UnreadCountAsync());
    }

    [Fact]
    public async Task Notification_links_must_stay_inside_the_app()
    {
        var w = await WorldAsync();
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var n = s.Get<NotificationService>();
            n.Notify(w.Rep.Id, "test", "Evil", null, "https://evil.example.com/phish");
            n.Notify(w.Rep.Id, "test", "Sneaky", null, "//evil.example.com");
            n.Notify(w.Rep.Id, "test", "Fine", null, "/Tasks");
            await s.Get<OesDbContext>().SaveChangesAsync();
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
        {
            var all = await s.Get<NotificationService>().LatestAsync();
            Assert.Null(all.Single(x => x.Title == "Evil").Url);
            Assert.Null(all.Single(x => x.Title == "Sneaky").Url);
            Assert.Equal("/Tasks", all.Single(x => x.Title == "Fine").Url);
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin)) // can't read someone else's
        {
            Assert.Empty(await s.Get<NotificationService>().LatestAsync());
        }
    }
}
