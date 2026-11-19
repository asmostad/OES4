using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class ProspectingTests(MySqlFixture fx)
{
    private static async Task<Guid> LeadAsync(AsyncServiceScope s, string name, Guid? stageId = null)
    {
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput
        {
            FirstName = name, LastName = "Caller", Methods = [new("mobile", "402-555-01" + Random.Shared.Next(10, 99), null, true), new("email", $"{name.ToLowerInvariant()}@example.test", null, true)],
        });
        return await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, StageId = stageId }); // unassigned pool
    }

    [Fact]
    public async Task Call_list_locks_leads_and_dispositions_decide_what_comes_back()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        Guid o1, o2, o3, inspected;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var stages = await s.Get<OpportunityService>().StagesAsync();
            o1 = await LeadAsync(s, "Ada");
            o2 = await LeadAsync(s, "Ben");
            o3 = await LeadAsync(s, "Cy");
            inspected = await LeadAsync(s, "Dee", stages.Single(x => x.Name == "Inspected").Id); // not a call-list stage
        }
        var repA = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var repB = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var all = new ProspectFilter();

        // ScopeAs sets an async-local user, so each step gets its own scope (and its own DbContext).
        async Task<T> As<T>(AppUser u, Func<ProspectingService, Task<T>> f) { await using var s = fx.ScopeAs(t.Id, u); return await f(s.Get<ProspectingService>()); }
        async Task Do(AppUser u, Func<ProspectingService, Task> f) { await using var s = fx.ScopeAs(t.Id, u); await f(s.Get<ProspectingService>()); }
        async Task<Opportunity> Load(Guid id) { await using var s = fx.ScopeAs(t.Id, admin); return await s.Get<OesDbContext>().Opportunities.SingleAsync(o => o.Id == id); }
        async Task Update(Guid id, DateTime? lockedAt, DateTime? recycleAt)
        {
            await using var s = fx.ScopeAs(t.Id, admin);
            var q = s.Get<OesDbContext>().Opportunities.Where(o => o.Id == id);
            if (lockedAt is { } l) await q.ExecuteUpdateAsync(x => x.SetProperty(o => o.LockedAt, l));
            if (recycleAt is { } r) await q.ExecuteUpdateAsync(x => x.SetProperty(o => o.RecycleAt, r));
        }
        var outcomes = (await As(repA, p => p.DispositionsAsync())).ToDictionary(d => d.Name);

        // Oldest first; a lead being worked is skipped by everyone else and can't be dispositioned by them.
        Assert.Equal(o1, await As(repA, p => p.NextAsync(all, [])));
        Assert.Equal(o2, await As(repB, p => p.NextAsync(all, [])));
        var locked = await Assert.ThrowsAsync<CrmRuleException>(() => Do(repB, p => p.ApplyAsync(o1, outcomes["No answer"].Id, null)));
        Assert.Contains("working this lead", locked.Message);

        await Do(repA, p => p.ApplyAsync(o1, outcomes["No answer"].Id, "Rang out"));
        Assert.Equal(o3, await As(repA, p => p.NextAsync(all, [])));
        await Do(repA, p => p.ApplyAsync(o3, outcomes["Inspection set"].Id, null));
        Assert.Null(await As(repA, p => p.NextAsync(all, []))); // o1 recycled, o2 is B's, o3 left the call-list stages, Dee was never on it

        var first = await Load(o1);
        Assert.Equal((1, (Guid?)null), (first.ContactAttempts, first.LockedByUserId));
        Assert.True(first.RecycleAt > DateTime.UtcNow.AddHours(23));
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var db = s.Get<OesDbContext>();
            var o3Stage = (await db.Opportunities.SingleAsync(o => o.Id == o3)).StageId;
            Assert.Equal("Inspection Scheduled", await db.PipelineStages.Where(x => x.Id == o3Stage).Select(x => x.Name).SingleAsync());
            Assert.Equal(1, await db.OpportunityDispositions.CountAsync(d => d.OpportunityId == o1 && d.Note == "Rang out"));
        }
        Assert.Equal(new ProspectingService.QueueCounts(1, 1, 1), await As(repA, p => p.CountsAsync(all)));

        // Back walks this rep's own call history.
        Assert.Equal(o3, await As(repA, p => p.PreviousAsync(null)));
        Assert.Equal(o1, await As(repA, p => p.PreviousAsync(o3)));
        Assert.Null(await As(repA, p => p.PreviousAsync(o1)));

        // An abandoned lock expires; skipped leads stay out of this run.
        await Update(o2, DateTime.UtcNow.AddMinutes(-20), null);
        await Update(o1, null, DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(o2, await As(repA, p => p.NextAsync(all, [o1])));

        // Do not contact: off the list for good, and opted out of marketing.
        await Do(repA, p => p.ApplyAsync(o2, outcomes["Do not contact"].Id, "Asked not to be called"));
        Assert.Equal(o1, await As(repA, p => p.NextAsync(all, [])));
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var contact2 = (await Load(o2)).PrimaryContactId;
            var consent = (await s.Get<ConsentService>().CurrentAsync(contact2)).Single(c => c.Channel == Channels.Sms && c.Purpose == MessagePurposes.Marketing);
            Assert.Equal(ConsentStatuses.OptedOut, consent.Status);
        }

        // A call-back day overrides the outcome's recycle days; the past isn't allowed.
        await Assert.ThrowsAsync<CrmRuleException>(() => Do(repA, p => p.ApplyAsync(o1, outcomes["Left voicemail"].Id, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)))));
        var nextWeek = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        await Do(repA, p => p.ApplyAsync(o1, outcomes["Left voicemail"].Id, null, nextWeek));
        Assert.Equal(nextWeek, DateOnly.FromDateTime((await Load(o1)).RecycleAt!.Value));

        // "Not interested" closes it as lost with its reason.
        await Do(repA, p => p.ApplyAsync(o1, outcomes["Not interested"].Id, null));
        var lost = await Load(o1);
        Assert.Equal((Outcomes.Lost, "not_interested", 3), (lost.Outcome, lost.LostReasonKey, lost.ContactAttempts));
        await Assert.ThrowsAsync<CrmRuleException>(() => Do(repA, p => p.ApplyAsync(o1, outcomes["No answer"].Id, null)));
        Assert.True(await As(repA, p => p.TryLockAsync(inspected))); // a lead off the list can still be opened and locked directly
    }

    [Fact]
    public async Task Call_outcomes_are_admin_settings_and_must_do_something()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var settings = s.Get<ListSettingsService>();
        var stages = await settings.StagesAsync();
        var lost = stages.Single(x => x.Outcome == Outcomes.Lost);

        await Assert.ThrowsAsync<CrmRuleException>(() => settings.SaveDispositionAsync(new DispositionInput { Name = "Hmm" }));
        await Assert.ThrowsAsync<CrmRuleException>(() => settings.SaveDispositionAsync(new DispositionInput { Name = "Went elsewhere", MovesToStageId = lost.Id }));
        await settings.SaveDispositionAsync(new DispositionInput { Name = "Went elsewhere", MovesToStageId = lost.Id, LostReasonKey = "competitor", CountsAsAttempt = true });
        Assert.Contains(await settings.DispositionsAsync(), d => d.Name == "Went elsewhere" && d.LostReasonKey == "competitor" && d.MovesToStageId == lost.Id);

        // Stages choose whether they feed the call list (the default stage and "Contacted" do for new companies).
        Assert.Equal(["New Lead", "Contacted"], stages.Where(x => x.InProspecting).Select(x => x.Name));
        var inspected = stages.Single(x => x.Name == "Inspected");
        await settings.SaveStageAsync(new StageInput { Id = inspected.Id, Name = inspected.Name, Color = inspected.Color, InProspecting = true });
        Assert.Contains(await s.Get<ProspectingService>().ProspectingStagesAsync(), x => x.Id == inspected.Id);

        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        await using var r = fx.ScopeAs(t.Id, rep);
        await Assert.ThrowsAsync<RecordForbiddenException>(() => r.Get<ListSettingsService>().SaveDispositionAsync(new DispositionInput { Name = "X", RecycleDays = 1 }));
    }

    [Fact]
    public async Task Reps_work_the_call_list_in_the_browser_and_the_app()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        Guid lead, other;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            lead = await LeadAsync(s, "Gwen");
            other = await LeadAsync(s, "Hal");
        }
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");

        // Browser: sign in, start calling, land on the first lead, pick an outcome, get the next one.
        var web = fx.App.Browser();
        var login = await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = rep.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, login.StatusCode);
        await web.GetAsync("/Account/SelectTenant");
        Assert.Contains("Due now", await web.GetStringAsync("/Prospecting"));

        var start = await web.GetAsync("/Prospecting/Work?mine=false");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, start.StatusCode);
        var url = start.Headers.Location!.OriginalString;
        Assert.Contains($"id={lead}", url);
        var page = await web.GetStringAsync(url);
        Assert.Contains("Gwen Caller", page);
        Assert.Contains("Left voicemail", page);

        Guid voicemail;
        await using (var s = fx.ScopeAs(t.Id, admin)) voicemail = (await s.Get<ProspectingService>().DispositionsAsync()).Single(d => d.Name == "Left voicemail").Id;
        var post = await web.PostAsync(url.Replace("/Prospecting/Work?", "/Prospecting/Work?handler=Disposition&"), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["dispositionId"] = voicemail.ToString(), ["note"] = "VM 1", ["__RequestVerificationToken"] = Token(page),
        }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, post.StatusCode);
        var next = await web.GetAsync(post.Headers.Location!.OriginalString);
        Assert.Contains($"id={other}", next.Headers.Location!.OriginalString);

        // App: the same list over the API (bearer token).
        var api = fx.App.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        var tok = await api.PostAsJsonAsync("/api/v1/auth/token", new { email = rep.Email, password = MySqlFixture.Password });
        var access = (await tok.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("accessToken").GetString();
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        var claimed = await api.PostAsync("/api/v1/prospecting/next", null);
        Assert.Equal(other, (await claimed.Content.ReadFromJsonAsync<NextLeadDto>())!.OpportunityId); // the web lock on Hal is the same user's
        Assert.Equal(System.Net.HttpStatusCode.NoContent,
            (await api.PostAsJsonAsync($"/api/v1/prospecting/{other}/disposition", new { dispositionId = voicemail, note = "VM" })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await api.PostAsync("/api/v1/prospecting/next", null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.UnprocessableEntity,
            (await api.PostAsJsonAsync($"/api/v1/prospecting/{other}/disposition", new { dispositionId = Guid.NewGuid() })).StatusCode);

        Assert.Contains("You're all caught up", await web.GetStringAsync("/Prospecting/Work"));

        // The opportunity page logs calls with an outcome; admins edit outcomes in Pipeline & Lists.
        Assert.Contains("Log call", await web.GetStringAsync($"/Opportunities/View/{lead}"));
        var adminWeb = fx.App.Browser();
        await adminWeb.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await adminWeb.GetStringAsync("/Account/Login")),
        }));
        await adminWeb.GetAsync("/Account/SelectTenant");
        var lists = await adminWeb.GetStringAsync("/Admin/Lists?list=dispositions");
        Assert.Contains("Do not contact", lists);
        Assert.Contains("Call back after (days)", lists);
        Assert.Contains("stage.InProspecting", await adminWeb.GetStringAsync("/Admin/Lists?list=stages"));
    }

    private sealed record NextLeadDto(Guid OpportunityId);

    private static string Token(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
}
