using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class TakeoffTests(MySqlFixture fx)
{
    [Fact]
    public async Task A_takeoff_turns_measurements_into_product_lines_and_reruns_replace_only_its_own()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var roofing = (await s.Get<MeasurementService>().TradesAsync()).Single(x => x.Code == "roofing");
        var takeoff = s.Get<TakeoffService>();

        // New companies get the starter rules (slots only, no products yet).
        var rules = await takeoff.RulesAsync(roofing.Id);
        Assert.Contains(rules, r => r.Rule.Name == "Shingles" && r.Rule.Slot == "shingle" && r.Rule.UseSuggestedWaste);

        var catalog = s.Get<CatalogService>();
        var arch = await catalog.SaveProductAsync(null, new ProductInput { Sku = "ARCH", Name = "Architectural shingle", Unit = "SQ", DefaultPrice = 130m, TradeTypeId = roofing.Id, TakeoffSlot = "Shingle" });
        var arch2 = await catalog.SaveProductAsync(null, new ProductInput { Sku = "ARCH2", Name = "Designer shingle", Unit = "SQ", DefaultPrice = 190m, TakeoffSlot = "shingle" });
        await catalog.SaveProductAsync(null, new ProductInput { Sku = "RIDGE", Name = "Ridge cap", Unit = "BD", DefaultPrice = 60m, TakeoffSlot = "ridge_cap", TakeoffMultiplier = 1m / 33 });
        await catalog.SaveProductAsync(null, new ProductInput { Sku = "DRIP", Name = "Drip edge 10'", Unit = "EA", DefaultPrice = 9m, TakeoffSlot = "drip_edge" });
        await Assert.ThrowsAsync<CrmRuleException>(() => catalog.SaveProductAsync(null, new ProductInput { Sku = "BAD", Name = "Bad", DefaultPrice = 1, TakeoffMultiplier = 0 }));

        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "5 Takeoff Ct", City = "Lincoln", State = "NE", PostalCode = "68502" });
        var structure = (await s.Get<PropertyService>().GetAsync(property)).Structures.Single();
        await s.Get<MeasurementService>().SaveVersionAsync(property, structure.Id, roofing.Id, new MeasurementInput
        {
            Values = new() { ["|total_area"] = 2805m, ["|ridges"] = 40m, ["|hips"] = 60m, ["|eaves"] = 120m, ["|rakes"] = 80m, ["|layers"] = 1m },
        });
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Tay", LastName = "Koff" });
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property });
        var est = s.Get<EstimateService>();
        var id = await est.CreateAsync(new NewEstimateInput { OpportunityId = opp, Method = EstimateMethods.LineItem });
        var section = (await est.GetAsync(id)).Sections.Single().Section.Id;
        var handLine = await est.AddMiscLineAsync(id, section, "Haul-away", 1, "EA", 250m, false);

        // Defaults: the ridge and drip edge slots have one product each, so they're picked; shingles need a choice.
        var run = await takeoff.ForSectionAsync(id, section);
        Assert.True(run.HasMeasurements);
        Assert.Equal(2, run.SlotProducts["shingle"].Count);
        Assert.Contains(run.Lines, l => l.Rule.Name == "Shingles" && l.Problem != null);
        Assert.Equal(4m, run.Lines.Single(l => l.Rule.Name == "Ridge cap").Qty); // product coverage: 100 LF / 33
        Assert.Contains(run.Options, o => o.Key == "valley_metal" && !o.DefaultOn);

        var choices = run.Choices;
        choices.Slots["shingle"] = arch;
        Assert.Equal(3, await takeoff.ApplyAsync(id, section, choices)); // shingles, ridge cap, drip edge
        var lines = (await est.GetAsync(id)).Sections.Single().Lines;
        var shingles = lines.Single(l => l.ProductId == arch);
        Assert.Equal((31.02m, 130m, true), (shingles.Qty, shingles.UnitPrice, shingles.TakeoffRuleId != null)); // 28.05 SQ + 10%, to the bundle
        Assert.Equal(21m, lines.Single(l => l.Code == "DRIP").Qty);           // 200 LF × 0.1 × 1.05
        Assert.Contains(lines, l => l.Id == handLine);

        // Re-running replaces only the takeoff's own lines, and remembers the picks.
        choices.Slots["shingle"] = arch2;
        await takeoff.ApplyAsync(id, section, choices);
        var after = (await est.GetAsync(id)).Sections.Single().Lines;
        Assert.Equal(4, after.Count);
        Assert.DoesNotContain(after, l => l.ProductId == arch);
        Assert.Contains(after, l => l.Id == handLine);
        Assert.Equal(arch2, (await takeoff.ForSectionAsync(id, section)).Choices.Slots["shingle"]);
        Assert.Equal(after.Sum(l => l.Amount), (await est.GetAsync(id)).Estimate.Subtotal);

        // The admin page, the rule editor and the takeoff page render; a rep can't edit rules.
        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = admin.Email!, ["Password"] = MySqlFixture.Password, ["__RequestVerificationToken"] = Token(await web.GetStringAsync("/Account/Login")),
        }));
        await web.GetAsync("/Account/SelectTenant");
        Assert.Contains("slot: shingle", await web.GetStringAsync("/Admin/Takeoff?trade=roofing"));
        var ruleHtml = await web.GetStringAsync("/Admin/TakeoffRule?trade=roofing");
        Assert.Contains("value=\"ridge_hip\"", ruleHtml);
        var page = await web.GetStringAsync($"/Estimates/Takeoff/{id}?section={section}");
        Assert.Contains("Designer shingle", page);
        Assert.Contains("Put these on the estimate", page);
        var apply = await web.PostAsync($"/Estimates/Takeoff/{id}?handler=Apply", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["section"] = section.ToString(), ["Choices.Slots[shingle]"] = arch.ToString(), ["Choices.Slots[drip_edge]"] = "", ["__RequestVerificationToken"] = Token(page),
        }));
        Assert.Equal(HttpStatusCode.Redirect, apply.StatusCode);
        var posted = (await s.Get<EstimateService>().GetAsync(id)).Sections.Single().Lines;
        Assert.Equal(["Architectural shingle", "Haul-away"], posted.Select(l => l.Description).Order()); // drip edge skipped; ridge slot not sent
        Assert.Contains($"/Estimates/Takeoff/{id}?section={section}", await web.GetStringAsync($"/Estimates/View/{id}"));

        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        await using var r = fx.ScopeAs(t.Id, rep);
        await Assert.ThrowsAsync<RecordForbiddenException>(() => r.Get<TakeoffService>().SaveRuleAsync(new TakeoffRuleInput { TradeTypeId = roofing.Id, Name = "X", Slot = "x" }));
    }

    [Fact]
    public async Task Rules_are_validated()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var gutters = (await s.Get<MeasurementService>().TradesAsync()).Single(x => x.Code == "gutters");
        var takeoff = s.Get<TakeoffService>();
        TakeoffRuleInput Valid() => new() { TradeTypeId = gutters.Id, Name = "Hangers", Slot = "Hanger!", Driver = "gutter_length", Multiplier = 0.5m };

        await Assert.ThrowsAsync<CrmRuleException>(() => takeoff.SaveRuleAsync(new TakeoffRuleInput { TradeTypeId = gutters.Id, Name = "No product", Driver = "gutter_length" }));
        var bad = Valid(); bad.Driver = "total_area"; // a roofing field
        await Assert.ThrowsAsync<CrmRuleException>(() => takeoff.SaveRuleAsync(bad));
        bad = Valid(); bad.Cond1Field = "downspouts"; bad.Cond1Op = "gte"; // no value
        await Assert.ThrowsAsync<CrmRuleException>(() => takeoff.SaveRuleAsync(bad));
        bad = Valid(); bad.MinQty = 5; bad.MaxQty = 2;
        await Assert.ThrowsAsync<CrmRuleException>(() => takeoff.SaveRuleAsync(bad));

        var id = await takeoff.SaveRuleAsync(Valid());
        var rule = await takeoff.GetRuleAsync(id);
        Assert.Equal(("hanger", (Guid?)null), (rule.Slot, rule.ProductId));
        var last = (await takeoff.RulesAsync(gutters.Id)).Last();
        Assert.Equal(id, last.Rule.Id);
        await takeoff.MoveRuleAsync(id, -1);
        Assert.NotEqual(id, (await takeoff.RulesAsync(gutters.Id)).Last().Rule.Id);
        Assert.True(await s.Get<OesDbContext>().AuditEntries.AnyAsync(a => a.Action == "takeoff_rule.created"));
    }

    private static string Token(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
}
