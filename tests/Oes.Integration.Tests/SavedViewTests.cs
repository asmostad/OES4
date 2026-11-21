using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Identity;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class SavedViewTests(MySqlFixture fx)
{
    private static Dictionary<string, string?> Q(params (string K, string V)[] kv) => kv.ToDictionary(x => x.K, x => (string?)x.V);

    [Fact]
    public async Task Views_keep_only_known_filters_and_sharing_is_for_managers()
    {
        var (t, _) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var other = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var manager = await fx.AddMemberAsync(t.Id, "Sales Manager");
        async Task<T> As<T>(AppUser u, Func<SavedViewService, Task<T>> f) { await using var s = fx.ScopeAs(t.Id, u); return await f(s.Get<SavedViewService>()); }
        async Task Do(AppUser u, Func<SavedViewService, Task> f) { await using var s = fx.ScopeAs(t.Id, u); await f(s.Get<SavedViewService>()); }

        var hail = await As(rep, v => v.SaveAsync("opportunities", " Hail leads ", Q(("q", "hail"), ("mine", "true"), ("p", "3"), ("bogus", "x"), ("outcome", "")), false, false));
        await Do(rep, v => v.SaveAsync("opportunities", "Hail leads", Q(("q", "hail st"), ("mine", "true")), false, false)); // same name: replaced
        var mine = await As(rep, v => v.ForListAsync("opportunities"));
        var row = Assert.Single(mine);
        Assert.Equal((hail, "Hail leads", "/Opportunities?q=hail%20st&mine=true", false), (row.Id, row.Name, row.Url, row.Shared));

        await Assert.ThrowsAsync<CrmRuleException>(() => Do(rep, v => v.SaveAsync("nowhere", "X", Q(), false, false)));
        await Assert.ThrowsAsync<CrmRuleException>(() => Do(rep, v => v.SaveAsync("contacts", "  ", Q(), false, false)));
        await Assert.ThrowsAsync<RecordForbiddenException>(() => Do(rep, v => v.SaveAsync("contacts", "Team", Q(), true, false)));

        // A manager's shared view shows for everyone; only managers can remove it, and it can't be anyone's default.
        var team = await As(manager, v => v.SaveAsync("opportunities", "Open insurance", Q(("outcome", "open")), true, false));
        var seen = await As(other, v => v.ForListAsync("opportunities"));
        Assert.Equal((team, true, false), (Assert.Single(seen).Id, seen[0].Shared, seen[0].CanDelete)); // not the other rep's personal view
        await Assert.ThrowsAsync<RecordForbiddenException>(() => Do(other, v => v.DeleteAsync(team)));
        await Assert.ThrowsAsync<CrmRuleException>(() => Do(other, v => v.SetDefaultAsync(team, true)));
        await Assert.ThrowsAsync<RecordNotFoundException>(() => Do(other, v => v.DeleteAsync(hail)));
        await Assert.ThrowsAsync<CrmRuleException>(() => Do(manager, v => v.SaveAsync("opportunities", "Both", Q(), true, true)));

        // Defaults: one per list per person.
        Assert.Null(await As(rep, v => v.DefaultUrlForPathAsync("/Opportunities")));
        await Do(rep, v => v.SetDefaultAsync(hail, true));
        Assert.Equal("/Opportunities?q=hail%20st&mine=true", await As(rep, v => v.DefaultUrlForPathAsync("/opportunities/")));
        await Do(rep, v => v.SaveAsync("opportunities", "Everything", Q(), false, true));
        Assert.Equal("/Opportunities?reset=1", await As(rep, v => v.DefaultUrlForPathAsync("/Opportunities")));
        Assert.Single(await As(rep, v => v.ForListAsync("opportunities")), x => x.IsDefault);
        Assert.Null(await As(other, v => v.DefaultUrlForPathAsync("/Opportunities")));

        await Do(manager, v => v.DeleteAsync(team));
        Assert.DoesNotContain(await As(rep, v => v.ForListAsync("opportunities")), x => x.Shared);
    }

    [Fact]
    public async Task Lists_open_with_the_default_view_and_reset_shows_everything()
    {
        var (t, _) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = rep.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");

        var page = await web.GetStringAsync("/Contacts?type=organization&status=lead&p=2");
        Assert.Contains("Save or manage views", page);
        var save = await web.PostAsync("/SavedViews?handler=Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["listKey"] = "contacts", ["name"] = "Org leads", ["isDefault"] = "true", ["returnUrl"] = "/Contacts?type=organization&status=lead&p=2",
            ["criteria[type]"] = "organization", ["criteria[status]"] = "lead", ["__RequestVerificationToken"] = Token(page),
        }));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        Assert.Equal("/Contacts?type=organization&status=lead", save.Headers.Location!.OriginalString);

        var open = await web.GetAsync("/Contacts");
        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        Assert.Equal("/Contacts?type=organization&status=lead", open.Headers.Location!.OriginalString);
        var filtered = await web.GetStringAsync("/Contacts?type=organization&status=lead");
        Assert.Contains("bi-bookmark me-1\"></i>Org leads", filtered); // the menu names the view on screen
        Assert.Equal(HttpStatusCode.OK, (await web.GetAsync("/Contacts?reset=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await web.GetAsync("/Properties")).StatusCode); // no default there
        foreach (var list in SavedViewService.Lists)
            Assert.Contains("Save or manage views", await web.GetStringAsync(list.Path + "?reset=1"));
    }

    private static string Token(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
}
