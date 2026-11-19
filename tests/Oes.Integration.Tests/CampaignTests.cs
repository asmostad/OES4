using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class CampaignTests(MySqlFixture fx)
{
    private static async Task<Guid> LeadAsync(AsyncServiceScope s, string name, string zip, string? stageName = null)
    {
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = name, LastName = "Zip" + zip });
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = $"{Random.Shared.Next(100, 9999)} {name} St", City = "Lincoln", State = "NE", PostalCode = zip + "-1234" });
        var stage = stageName is null ? null : (Guid?)(await s.Get<OpportunityService>().StagesAsync()).Single(x => x.Name == stageName).Id;
        return await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property, StageId = stage, SourceKey = "door_knock" });
    }

    [Fact]
    public async Task A_fixed_list_campaign_tracks_calls_wins_and_losses()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        Guid a, b, late, campaign;
        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            a = await LeadAsync(s, "Amy", "68516");
            b = await LeadAsync(s, "Bob", "68516");
            await LeadAsync(s, "Cal", "68516", "Estimate Sent"); // any stage: a campaign picks its own leads
            await LeadAsync(s, "Dot", "68502");                  // other ZIP

            var campaigns = s.Get<CampaignService>();
            var criteria = new CampaignCriteria { PostalCodes = ["68516, 68516 "] };
            Assert.Equal(3, await campaigns.PreviewAsync(criteria));
            campaign = await campaigns.SaveAsync(new CampaignInput { Name = "Hail 68516", Mode = CampaignModes.Static, Criteria = criteria });
            var d = await campaigns.GetAsync(campaign);
            Assert.Equal(new CampaignStats(3, 3, 0, 0, 0, 3), d.Stats);
            Assert.Equal(["68516"], d.Criteria.PostalCodes);
        }

        // A rep works the campaign from the call list.
        async Task<T> AsRep<T>(Func<ProspectingService, Task<T>> f) { await using var s = fx.ScopeAs(t.Id, rep); return await f(s.Get<ProspectingService>()); }
        var filter = new ProspectFilter(CampaignId: campaign);
        var outcomes = (await AsRep(p => p.DispositionsAsync())).ToDictionary(d => d.Name);
        Assert.Equal(a, await AsRep(p => p.NextAsync(filter, [])));
        await AsRep(async p => { await p.ApplyAsync(a, outcomes["No answer"].Id, null); return 0; });
        Assert.Equal(b, await AsRep(p => p.NextAsync(filter, [])));
        await AsRep(async p => { await p.ApplyAsync(b, outcomes["Not interested"].Id, null); return 0; });
        Assert.Equal(1, (await AsRep(p => p.CountsAsync(filter))).Due); // only Cal is left

        await using (var s = fx.ScopeAs(t.Id, admin))
        {
            var campaigns = s.Get<CampaignService>();
            late = await LeadAsync(s, "Eve", "68516");
            var d = await campaigns.GetAsync(campaign);
            Assert.Equal(new CampaignStats(3, 1, 2, 0, 1, 2), d.Stats); // the new lead isn't in the fixed list yet
            Assert.Equal(1, d.NewMatches);
            Assert.Equal([("No answer", 1), ("Not interested", 1)], d.Outcomes.Select(o => (o.Name, o.Count)));

            Assert.Equal(1, await campaigns.AddMatchesAsync(campaign));
            Assert.Equal(4, (await campaigns.GetAsync(campaign)).Stats.Leads);
            await campaigns.RemoveLeadAsync(campaign, late);
            Assert.Equal(3, (await campaigns.GetAsync(campaign)).Stats.Leads);
            await Assert.ThrowsAsync<CrmRuleException>(() => campaigns.SaveAsync(new CampaignInput { Id = campaign, Name = "Hail 68516", Mode = CampaignModes.Dynamic }));
        }

        await using (var r = fx.ScopeAs(t.Id, rep))
        {
            Assert.Contains(await r.Get<CampaignService>().ActiveAsync(), c => c.Id == campaign); // reps can pick it
            await Assert.ThrowsAsync<RecordForbiddenException>(() => r.Get<CampaignService>().SaveAsync(new CampaignInput { Name = "Mine" }));
            await Assert.ThrowsAsync<RecordForbiddenException>(() => r.Get<CampaignService>().GetAsync(campaign));
        }
    }

    [Fact]
    public async Task A_live_campaign_follows_its_filters()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var fresh = await LeadAsync(s, "Fay", "68510");
        var called = await LeadAsync(s, "Gus", "68510");
        var p = s.Get<ProspectingService>();
        var outcomes = (await p.DispositionsAsync()).ToDictionary(d => d.Name);
        await p.ApplyAsync(called, outcomes["Left voicemail"].Id, null);

        var campaigns = s.Get<CampaignService>();
        await Assert.ThrowsAsync<CrmRuleException>(() => campaigns.SaveAsync(new CampaignInput { Name = " " }));
        await Assert.ThrowsAsync<CrmRuleException>(() => campaigns.SaveAsync(new CampaignInput
            { Name = "Bad dates", Criteria = new() { CreatedFrom = new DateOnly(2026, 5, 2), CreatedTo = new DateOnly(2026, 5, 1) } }));
        var id = await campaigns.SaveAsync(new CampaignInput { Name = "Never called", Mode = CampaignModes.Dynamic, Criteria = new() { MaxAttempts = 0, SourceKey = "door_knock" } });
        Assert.Equal(1, (await campaigns.GetAsync(id)).Stats.Leads);
        Assert.Equal(fresh, await p.NextAsync(new ProspectFilter(CampaignId: id), []));
        await Assert.ThrowsAsync<CrmRuleException>(() => campaigns.AddMatchesAsync(id));
    }

    [Fact]
    public async Task Campaign_pages_render_and_create()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using (var s = fx.ScopeAs(t.Id, admin)) await LeadAsync(s, "Hal", "68520");
        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");

        Assert.Contains("No campaigns yet", await web.GetStringAsync("/Campaigns"));
        var form = await web.GetStringAsync("/Campaigns/Edit");
        var preview = await web.PostAsync("/Campaigns/Edit?handler=Preview", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "North side", ["Input.Mode"] = "static", ["Input.IsActive"] = "true", ["Zips"] = "68520", ["__RequestVerificationToken"] = Token(form),
        }));
        Assert.Contains("<strong>1</strong> open lead match", await preview.Content.ReadAsStringAsync());
        var save = await web.PostAsync("/Campaigns/Edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "North side", ["Input.Mode"] = "static", ["Input.IsActive"] = "true", ["Zips"] = "68520", ["__RequestVerificationToken"] = Token(form),
        }));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var view = await web.GetStringAsync(save.Headers.Location!.OriginalString);
        Assert.Contains("ZIP codes: 68520", view);
        Assert.Contains("Hal Zip68520", view);
        Assert.Contains("North side", await web.GetStringAsync("/Prospecting"));
        Assert.Contains("North side", await web.GetStringAsync("/Campaigns"));
    }

    private static string Token(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
}
