using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Admin;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed partial class NurtureTests(MySqlFixture fx)
{
    private sealed record World(Guid Tenant, AppUser Admin, AppUser Rep, Guid Contact, Guid Opportunity, string Mobile, string Email);

    private static int _n;

    // 9 am in Chicago (CST) on a weekday in December.
    private static readonly DateTime Morning = new(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc);

    [GeneratedRegex(@"/portal/u/([A-Za-z0-9_\-]+)")]
    private static partial Regex UnsubscribeLink();

    private async Task<World> WorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var n = Interlocked.Increment(ref _n);
        var mobile = $"+1402556{n:0000}";
        var email = $"nora{n}.{Guid.NewGuid():N}"[..20] + "@example.com";
        await using var s = fx.ScopeAs(t.Id, admin);
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput
        {
            FirstName = "Nora", LastName = "Nurture", OwnerUserId = rep.Id,
            Methods = [new MethodInput(ContactMethodKinds.Email, email, null, true), new MethodInput(ContactMethodKinds.Mobile, mobile, null, true)],
        });
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, OwnerUserId = rep.Id });
        await s.Get<SettingsService>().SaveAsync(new CompanySettings { Name = t.Name, TimeZone = "America/Chicago", PostalAddress = "PO Box 9, Lincoln, NE" });
        await s.Get<MessagingService>().SetPhoneNumberAsync("+1531556" + mobile[^4..]);
        await s.Get<ConsentService>().RecordAsync(contact, Channels.Sms, MessagePurposes.Transactional, ConsentStatuses.OptedIn, "verbal", null);
        return new World(t.Id, admin, rep, contact, opp, mobile, email);
    }

    private async Task<Guid> SequenceAsync(World w, string category, params StepInput[] steps)
    {
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var nurture = s.Get<NurtureService>();
        var id = await nurture.SaveAsync(null, new SequenceInput { Name = $"Follow-up {Guid.NewGuid():N}"[..20], Category = category });
        foreach (var step in steps) await nurture.AddStepAsync(id, step);
        return id;
    }

    private static StepInput Email(int days = 0) => new() { Kind = StepKinds.Email, DelayDays = days, Subject = "Your estimate, {{contact.first_name}}", Body = "Hi {{contact.first_name}}, any questions?\n{{user.first_name}}" };
    private static StepInput Text(int days = 0) => new() { Kind = StepKinds.Sms, DelayDays = days, Body = "Hi {{contact.first_name}}, just checking in." };
    private static StepInput Task(int days = 0) => new() { Kind = StepKinds.Task, DelayDays = days, Subject = "Call {{contact.name}}" };

    private async Task<Guid> EnrollAsync(World w, Guid sequence)
    {
        await using var s = fx.ScopeAs(w.Tenant, w.Rep);
        return await s.Get<NurtureService>().EnrollAsync(sequence, w.Contact, w.Opportunity);
    }

    private async Task RunAsync(DateTime at)
    {
        fx.Clock.Pinned = at;
        await using (fx.ScopeFor(null)) { }
        await NurtureRunner.RunDueAsync(fx.App.Services.GetRequiredService<IServiceScopeFactory>());
    }

    private async Task<Enrollment> EnrollmentAsync(World w, Guid id)
    {
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        return await s.Get<OesDbContext>().Enrollments.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    [Fact]
    public async Task Steps_run_in_order_after_their_waits_from_the_owner_and_land_on_the_opportunity()
    {
        var w = await WorldAsync();
        var seq = await SequenceAsync(w, MessagePurposes.Transactional, Email(), Text(days: 2), Task(days: 1));
        try
        {
            fx.Clock.Pinned = Morning;
            var id = await EnrollAsync(w, seq);
            await RunAsync(Morning);
            var e = await EnrollmentAsync(w, id);
            Assert.Equal((1, Morning.AddDays(2)), (e.StepIndex, e.NextRunAt!.Value));

            await RunAsync(Morning.AddDays(1)); // not due yet
            Assert.Equal(1, (await EnrollmentAsync(w, id)).StepIndex);
            await RunAsync(Morning.AddDays(2));
            await RunAsync(Morning.AddDays(3));
            e = await EnrollmentAsync(w, id);
            Assert.Equal((EnrollmentStatuses.Completed, 3), (e.Status, e.StepIndex));

            await using var s = fx.ScopeAs(w.Tenant, w.Admin);
            var db = s.Get<OesDbContext>();
            var email = await db.EmailMessages.SingleAsync(m => m.Recipients.Any(r => r.ContactId == w.Contact));
            Assert.Equal(("Your estimate, Nora", w.Rep.Id), (email.Subject, email.SentByUserId!.Value)); // from the opportunity owner
            Assert.DoesNotContain("Unsubscribe", email.Body); // everyday workflow: no marketing footer
            var text = await db.SmsMessages.SingleAsync(m => m.ToNumber == w.Mobile);
            Assert.Equal("Hi Nora, just checking in.\n" + MessagingService.OptOutFooter, text.Body);
            var task = await db.Tasks.SingleAsync(t => t.EntityId == w.Opportunity);
            Assert.Equal(("Call Nora Nurture", w.Rep.Id), (task.Title, task.AssignedToUserId));
            Assert.Equal(3, await db.EnrollmentStepRuns.CountAsync(r => r.EnrollmentId == id && r.Status == StepRunStatuses.Done));
            var onOpp = await s.Get<RecordItems>().TimelineAsync(EntityTypes.Opportunity, w.Opportunity, TimelineScope.ThisRecord);
            Assert.Contains(onOpp, i => i.ItemType == ItemTypes.Email);
            Assert.Contains(onOpp, i => i.ItemType == ItemTypes.Sms);
        }
        finally { fx.Clock.Pinned = null; }
    }

    [Fact]
    public async Task Texts_wait_for_daytime_and_replies_or_a_closed_opportunity_stop_the_workflow()
    {
        var w = await WorldAsync();
        var seq = await SequenceAsync(w, MessagePurposes.Transactional, Text(), Email(days: 1));
        var late = new DateTime(2026, 12, 2, 4, 0, 0, DateTimeKind.Utc); // 10 pm in Chicago
        try
        {
            fx.Clock.Pinned = late;
            var id = await EnrollAsync(w, seq);
            await RunAsync(late);
            var e = await EnrollmentAsync(w, id);
            Assert.Equal((0, new DateTime(2026, 12, 2, 15, 0, 0)), (e.StepIndex, e.NextRunAt!.Value)); // 9 am next morning
            await RunAsync(new DateTime(2026, 12, 2, 15, 0, 0));
            Assert.Equal(1, (await EnrollmentAsync(w, id)).StepIndex);

            // They text back before the email goes out.
            await using (fx.ScopeFor(null)) { }
            await using (var s = fx.ScopeFor(null))
                await s.Get<MessagingService>().ReceiveSmsAsync(w.Mobile, "+1531556" + w.Mobile[^4..], "Sounds good, call me", null);
            await RunAsync(new DateTime(2026, 12, 3, 16, 0, 0));
            e = await EnrollmentAsync(w, id);
            Assert.Equal((EnrollmentStatuses.Stopped, "They replied."), (e.Status, e.StopReason));

            // A won opportunity stops a fresh enrollment too.
            fx.Clock.Pinned = new DateTime(2026, 12, 4, 15, 0, 0);
            var second = await SequenceAsync(w, MessagePurposes.Transactional, Email());
            var id2 = await EnrollAsync(w, second);
            await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
                await s.Get<OesDbContext>().Opportunities.Where(o => o.Id == w.Opportunity).ExecuteUpdateAsync(u => u.SetProperty(o => o.Outcome, Outcomes.Won));
            await RunAsync(new DateTime(2026, 12, 4, 15, 0, 0));
            Assert.Equal("The opportunity was won.", (await EnrollmentAsync(w, id2)).StopReason);
        }
        finally { fx.Clock.Pinned = null; }
    }

    [Fact]
    public async Task Marketing_workflows_need_marketing_consent_carry_an_unsubscribe_link_and_stop_when_used()
    {
        var w = await WorldAsync();
        var seq = await SequenceAsync(w, MessagePurposes.Marketing, Text(), Email(), Email(days: 7));
        try
        {
            fx.Clock.Pinned = Morning;
            var id = await EnrollAsync(w, seq);
            await RunAsync(Morning); // text: only a transactional opt-in on file, so skipped
            await RunAsync(Morning); // email with footer
            Guid emailId;
            await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
            {
                var db = s.Get<OesDbContext>();
                var runs = await db.EnrollmentStepRuns.Where(r => r.EnrollmentId == id).OrderBy(r => r.StepIndex).ToListAsync();
                Assert.Equal((StepRunStatuses.Skipped, "No marketing text opt-in on file."), (runs[0].Status, runs[0].Detail));
                Assert.Equal(StepRunStatuses.Done, runs[1].Status);
                emailId = runs[1].ItemId!.Value;
            }
            var file = Directory.GetFiles(Path.Combine(fx.FilesRoot, "messages", "email"), "*.html").Select(File.ReadAllText).Single(f => f.Contains(w.Email));
            Assert.Contains("PO Box 9, Lincoln, NE", file);
            var token = UnsubscribeLink().Match(file).Groups[1].Value;

            await using (var s = fx.ScopeFor(null)) Assert.True(await s.Get<BlastService>().UnsubscribeAsync(token));
            var e = await EnrollmentAsync(w, id);
            Assert.Equal((EnrollmentStatuses.Stopped, "Unsubscribed."), (e.Status, e.StopReason));
            await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
                Assert.Equal("They unsubscribed.", await s.Get<ConsentService>().BlockReasonAsync(w.Contact, Channels.Email, w.Email, MessagePurposes.Marketing));
        }
        finally { fx.Clock.Pinned = null; }
    }

    [Fact]
    public async Task Enrolling_rules_and_permissions()
    {
        var w = await WorldAsync();
        var seq = await SequenceAsync(w, MessagePurposes.Transactional, Email(days: 1));
        await using var s = fx.ScopeAs(w.Tenant, w.Rep);
        var nurture = s.Get<NurtureService>();
        await Assert.ThrowsAsync<RecordForbiddenException>(() => nurture.SaveAsync(null, new SequenceInput { Name = "Mine" }));
        var id = await nurture.EnrollAsync(seq, w.Contact, w.Opportunity);
        await Assert.ThrowsAsync<CrmRuleException>(() => nurture.EnrollAsync(seq, w.Contact, w.Opportunity)); // already in it
        await nurture.StopAsync(id);
        Assert.Equal(EnrollmentStatuses.Stopped, (await s.Get<OesDbContext>().Enrollments.AsNoTracking().SingleAsync(e => e.Id == id)).Status);

        var other = await fx.AddMemberAsync(w.Tenant, "Sales Rep");
        await using var o = fx.ScopeAs(w.Tenant, other);
        await Assert.ThrowsAsync<RecordNotFoundException>(() => o.Get<NurtureService>().EnrollAsync(seq, w.Contact, w.Opportunity)); // not their record

        await using var a = fx.ScopeAs(w.Tenant, w.Admin);
        await Assert.ThrowsAsync<CrmRuleException>(() => a.Get<NurtureService>().AddStepAsync(seq, new StepInput { Kind = StepKinds.Email, Subject = "x", Body = "{{contact.nickname}}" }));
    }
}
