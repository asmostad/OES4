using System.Net;
using Oes.Infrastructure.Admin;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class SettingsPageTests(MySqlFixture fx)
{
    [Fact]
    public async Task Admins_set_estimate_defaults_on_the_settings_page()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");

        var page = await web.GetStringAsync("/Admin/Settings");
        Assert.Contains("Estimates &amp; proposals", page);
        Assert.Contains("name=\"Estimates.ValidDays\" value=\"30\"", page);

        var bad = await web.PostAsync("/Admin/Settings?handler=Estimates", Form(page, ("Estimates.ValidDays", "0"), ("Estimates.TaxRate", "0"),
            ("Estimates.InsuranceOverheadPct", "10"), ("Estimates.InsuranceProfitPct", "10")));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("valid for 1 to 365 days", await bad.Content.ReadAsStringAsync());

        var ok = await web.PostAsync("/Admin/Settings?handler=Estimates", Form(page, ("Estimates.Terms", "Net 10"), ("Estimates.ValidDays", "60"),
            ("Estimates.TaxRate", "7.25"), ("Estimates.InsuranceOverheadPct", "10"), ("Estimates.InsuranceProfitPct", "10")));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        await using var s = fx.ScopeAs(t.Id, admin);
        var saved = await s.Get<SettingsService>().EstimateDefaultsAsync();
        Assert.Equal(("Net 10", 60, 7.25m), (saved.Terms, saved.ValidDays, saved.TaxRate));
    }

    private static FormUrlEncodedContent Form(string page, params (string K, string V)[] fields) =>
        new(fields.ToDictionary(f => f.K, f => f.V).Append(new("__RequestVerificationToken", Token(page))));

    private static string Token(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
}
