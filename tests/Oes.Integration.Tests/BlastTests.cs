using System.Net;
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
public sealed partial class BlastTests(MySqlFixture fx)
{
    private sealed record World(Guid Tenant, AppUser Admin, Dictionary<string, Guid> Contacts, string Tag);

    [GeneratedRegex(@"/portal/u/([A-Za-z0-9_\-]+)")]
    private static partial Regex UnsubscribeLink();

    /// <summary>
    /// Customers ann/bob/cat/dee (+ a duplicate of ann's address), lead len, and customer nia with no email.
    /// bob already unsubscribed from marketing; cat opted out of email entirely.
    /// </summary>
    private async Task<World> WorldAsync(bool withAddress = true)
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var ids = new Dictionary<string, Guid>();
        await using var s = fx.ScopeAs(t.Id, admin);
        var contacts = s.Get<ContactService>();
        async Task Add(string first, string? email, string status)
        {
            var methods = email is null ? new List<MethodInput>() : [new MethodInput(ContactMethodKinds.Email, email, null, true)];
            var id = await contacts.CreateAsync(new ContactInput { FirstName = first, LastName = "Blast", OwnerUserId = admin.Id, Methods = methods });
            ids[first] = id;
            await s.Get<OesDbContext>().Contacts.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, status));
        }
        await Add("Ann", $"ann.{tag}@example.com", ContactStatuses.Customer);
        await Add("Annie", $"ANN.{tag}@example.com", ContactStatuses.PastCustomer); // same mailbox as Ann
        await Add("Bob", $"bob.{tag}@example.com", ContactStatuses.Customer);
        await Add("Cat", $"cat.{tag}@example.com", ContactStatuses.Customer);
        await Add("Dee", $"dee.{tag}@example.com", ContactStatuses.Customer);
        await Add("Len", $"len.{tag}@example.com", ContactStatuses.Lead);
        await Add("Nia", null, ContactStatuses.Customer);

        var db = s.Get<OesDbContext>();
        db.Suppressions.Add(new Suppression { Channel = Channels.Email, Address = $"bob.{tag}@example.com", Scope = "marketing", Reason = SuppressionReasons.Unsubscribe });
        await db.SaveChangesAsync();
        await s.Get<ConsentService>().RecordAsync(ids["Cat"], Channels.Email, MessagePurposes.Transactional, ConsentStatuses.OptedOut, "verbal", null);
        if (withAddress)
            await s.Get<SettingsService>().SaveAsync(new CompanySettings { Name = t.Name, TimeZone = "America/Chicago", PostalAddress = "PO Box 1, Lincoln, NE 68501" });
        return new World(t.Id, admin, ids, tag);
    }

    private static BlastInput Input(int throttle = 30) => new()
    {
        Name = "Spring check", Subject = "Spring roof check for {{contact.first_name}}", Body = "Hi {{contact.first_name}},\nTime for a spring check.\n{{user.name}}",
        Audience = new BlastAudience { Statuses = [ContactStatuses.Customer, ContactStatuses.PastCustomer] }, ThrottlePerMinute = throttle,
    };

    private async Task RunSenderAsync()
    {
        await using (fx.ScopeFor(null)) { } // no signed-in user in the worker
        await BlastSender.RunDueAsync(fx.App.Services.GetRequiredService<IServiceScopeFactory>());
    }

    private async Task<Blast> BlastAsync(World w, Guid id)
    {
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        return await s.Get<OesDbContext>().Blasts.AsNoTracking().SingleAsync(b => b.Id == id);
    }

    private string PickedUpFor(string address) =>
        Directory.GetFiles(Path.Combine(fx.FilesRoot, "messages", "email"), "*.html").Select(File.ReadAllText)
            .Single(f => f.Contains($"To: {address}") && f.Contains("Spring roof check"));

    [Fact]
    public async Task Audience_leaves_out_opt_outs_duplicates_and_missing_email_and_sends_in_throttled_batches()
    {
        var w = await WorldAsync();
        Guid id;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var blasts = s.Get<BlastService>();
            id = await blasts.SaveDraftAsync(null, Input(throttle: 1));
            var p = await blasts.PreviewAsync(Input().Audience);
            Assert.Equal(2, p.Eligible); // Ann, Dee
            Assert.Equal(3, p.Blocked);  // Annie (duplicate), Bob (unsubscribed), Cat (opted out)
            Assert.Equal(1, p.NoEmail);  // Nia
            Assert.Equal(2, await blasts.ScheduleAsync(id, null));
            await Assert.ThrowsAsync<CrmRuleException>(() => blasts.SaveDraftAsync(id, Input())); // locked once scheduled
            Assert.Contains(await blasts.RecipientsAsync(id, BlastRecipientStatuses.Skipped), r => r.ContactName == "Bob Blast" && r.Error == "They unsubscribed.");
        }

        await RunSenderAsync();
        var b = await BlastAsync(w, id);
        Assert.Equal((BlastStatuses.Sending, 1), (b.Status, b.SentCount));
        await RunSenderAsync();
        b = await BlastAsync(w, id);
        Assert.Equal((BlastStatuses.Sent, 2, 3), (b.Status, b.SentCount, b.SkippedCount));
        await RunSenderAsync(); // nothing left: no double send
        Assert.Equal(2, (await BlastAsync(w, id)).SentCount);

        var mail = PickedUpFor($"ann.{w.Tag}@example.com");
        Assert.Contains("Spring roof check for Ann", mail);
        Assert.Contains("Hi Ann,", mail);
        Assert.Contains("PO Box 1, Lincoln, NE 68501", mail);
        Assert.Contains("List-Unsubscribe-Post: List-Unsubscribe=One-Click", mail);
        Assert.Matches(UnsubscribeLink(), mail);

        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var onAnn = await s.Get<RecordItems>().TimelineAsync(EntityTypes.Contact, w.Contacts["Ann"], TimelineScope.Everything, ItemTypes.Email);
            Assert.Equal("Spring roof check for Ann", Assert.Single(onAnn).Message!.Subject);
        }
    }

    [Fact]
    public async Task Scheduling_needs_a_mailing_address_and_consent_is_checked_again_at_send_time()
    {
        var w = await WorldAsync(withAddress: false);
        Guid id;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var blasts = s.Get<BlastService>();
            id = await blasts.SaveDraftAsync(null, Input());
            Assert.Contains("mailing address", (await Assert.ThrowsAsync<CrmRuleException>(() => blasts.ScheduleAsync(id, null))).Message);
            await s.Get<SettingsService>().SaveAsync(new CompanySettings { Name = "Co", TimeZone = "America/Chicago", PostalAddress = "1 Main St" });
            await blasts.ScheduleAsync(id, null);
            // Dee opts out after the audience was locked in.
            await s.Get<ConsentService>().RecordAsync(w.Contacts["Dee"], Channels.Email, MessagePurposes.Marketing, ConsentStatuses.OptedOut, "verbal", null);
        }
        await RunSenderAsync();
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var dee = Assert.Single(await s.Get<BlastService>().RecipientsAsync(id, null), r => r.ContactName == "Dee Blast");
            Assert.Equal(BlastRecipientStatuses.Skipped, dee.Status);
            Assert.Equal((1, 4), ((await BlastAsync(w, id)).SentCount, (await BlastAsync(w, id)).SkippedCount));
        }
    }

    [Fact]
    public async Task Unsubscribe_link_works_with_one_click_and_only_stops_marketing()
    {
        var w = await WorldAsync();
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var blasts = s.Get<BlastService>();
            await blasts.ScheduleAsync(await blasts.SaveDraftAsync(null, Input()), null);
        }
        await RunSenderAsync();
        var token = UnsubscribeLink().Match(PickedUpFor($"dee.{w.Tag}@example.com")).Groups[1].Value;

        var client = fx.App.Browser();
        var page = await client.GetAsync($"/portal/u/{token}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Unsubscribe from", html);
        Assert.DoesNotContain($"dee.{w.Tag}@example.com", html); // address is masked
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/portal/u/not-a-token")).StatusCode);

        // RFC 8058 one-click: a bare POST from the mail app, no form token.
        var oneClick = await client.PostAsync($"/portal/u/{token}", new FormUrlEncodedContent(new Dictionary<string, string> { ["List-Unsubscribe"] = "One-Click" }));
        Assert.Equal(HttpStatusCode.OK, oneClick.StatusCode);
        Assert.Contains("unsubscribed", await (await client.GetAsync($"/portal/u/{token}")).Content.ReadAsStringAsync());

        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var consent = s.Get<ConsentService>();
            var dee = w.Contacts["Dee"];
            var address = $"dee.{w.Tag}@example.com";
            Assert.Equal("They unsubscribed.", await consent.BlockReasonAsync(dee, Channels.Email, address, MessagePurposes.Marketing));
            Assert.Null(await consent.BlockReasonAsync(dee, Channels.Email, address, MessagePurposes.Transactional)); // job email still fine
            await Assert.ThrowsAsync<CrmRuleException>(() => consent.RecordAsync(dee, Channels.Email, MessagePurposes.Marketing, ConsentStatuses.OptedIn, "verbal", null));
            await consent.RecordAsync(dee, Channels.Email, MessagePurposes.Transactional, ConsentStatuses.OptedIn, "verbal", null);
            var row = (await consent.SuppressionsAsync(Channels.Email, address)).Single();
            await Assert.ThrowsAsync<CrmRuleException>(() => consent.RemoveSuppressionAsync(row.Id));

            // The next blast leaves Dee out.
            var p = await s.Get<BlastService>().PreviewAsync(Input().Audience);
            Assert.Equal(1, p.Eligible);
        }
    }

    [Fact]
    public async Task Future_and_cancelled_blasts_are_not_sent_and_reps_cannot_manage_blasts()
    {
        var w = await WorldAsync();
        Guid later, cancelled;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var blasts = s.Get<BlastService>();
            later = await blasts.SaveDraftAsync(null, Input());
            await blasts.ScheduleAsync(later, DateTime.UtcNow.AddHours(2));
            cancelled = await blasts.SaveDraftAsync(null, Input());
            await blasts.ScheduleAsync(cancelled, null);
            await blasts.CancelAsync(cancelled);
            var past = await blasts.SaveDraftAsync(null, Input());
            await Assert.ThrowsAsync<CrmRuleException>(() => blasts.ScheduleAsync(past, DateTime.UtcNow.AddDays(-2)));
        }
        await RunSenderAsync();
        Assert.Equal((BlastStatuses.Scheduled, 0), ((await BlastAsync(w, later)).Status, (await BlastAsync(w, later)).SentCount));
        var c = await BlastAsync(w, cancelled);
        Assert.Equal((BlastStatuses.Cancelled, 0, 5), (c.Status, c.SentCount, c.SkippedCount));

        var rep = await fx.AddMemberAsync(w.Tenant, "Sales Rep");
        await using (var s = fx.ScopeAs(w.Tenant, rep))
            await Assert.ThrowsAsync<RecordForbiddenException>(() => s.Get<BlastService>().SaveDraftAsync(null, Input()));
    }
}
