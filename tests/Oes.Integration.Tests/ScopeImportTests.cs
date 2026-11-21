using System.Text;
using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class ScopeImportTests(MySqlFixture fx)
{
    private const string Csv =
        "category,code,description,unit,activity,unit_price,material\n" +
        "RFG,300S,Laminated shingles,SQ,replace,285.00,150\n" +
        "RFG,300S,Laminated shingles,SQ,remove,62.50,\n" +
        "RFG,DRIP,Drip edge,LF,replace,2.35,1.10\n";

    // Made-up scope lines for the fake model (not real carrier pricing).
    private const string ScopeJson = """
        {"carrier":"Sample Mutual","price_list":"TEST_LIST","notes":"Page 4 was blurry.",
         "lines":[
           {"category":"RFG","code":"300S","activity":"remove","description":"Tear off laminated shingles","quantity":25.5,"unit":"SQ","unit_price":60.00},
           {"category":"RFG","code":"300S","activity":"replace","description":"Laminated shingles","quantity":28.33,"unit":"SQ","unit_price":270.00},
           {"category":"RFG","code":"VENT","activity":"replace","description":"Ridge vent","quantity":40,"unit":"lf","unit_price":9.50},
           {"category":"RFG","code":"X","activity":"other","description":"","quantity":1,"unit":"EA","unit_price":1},
           {"category":"RFG","code":"Y","activity":"other","description":"Negative","quantity":-3,"unit":"EA","unit_price":1}
         ]}
        """;

    private sealed record World(Guid Tenant, AppUser Admin, Guid Estimate);

    private async Task<World> WorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var catalog = s.Get<CatalogService>();
        var listId = await catalog.SaveListAsync(null, new PriceListInput { Name = "Scope list", Status = PriceListStatuses.Active });
        await catalog.ImportCsvAsync(listId, new StringReader(Csv));
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "9 Scope St", City = "Lincoln", State = "NE", PostalCode = "68502" });
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Sco", LastName = "Pe", OwnerUserId = admin.Id });
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property, OwnerUserId = admin.Id, IsInsurance = true });
        var est = await s.Get<EstimateService>().CreateAsync(new NewEstimateInput { OpportunityId = opp, PriceListId = listId });
        return new World(t.Id, admin, est);
    }

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.4\n% fake scope\n"));

    [Fact]
    public async Task Scope_lines_are_matched_reviewed_and_only_ticked_ones_are_added()
    {
        var w = await WorldAsync();
        fx.Ai.Reset();
        fx.Ai.NextExtraction = (ScopeJson, "end_turn");
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var svc = s.Get<ScopeImportService>();
        var db = s.Get<OesDbContext>();
        var linesBefore = await db.EstimateLines.CountAsync(l => l.EstimateId == w.Estimate);

        var importId = await svc.ExtractAsync(w.Estimate, Pdf(), "scope.pdf");
        Assert.Equal(linesBefore, await db.EstimateLines.CountAsync(l => l.EstimateId == w.Estimate)); // nothing added yet
        Assert.True(await db.Documents.AnyAsync(d => d.EntityId == w.Estimate && d.CategoryKey == "carrier_scope"));
        Assert.True(fx.Ai.Extractions.Single().JsonSchema.GetProperty("properties").TryGetProperty("lines", out _));

        var view = await svc.GetAsync(importId);
        Assert.Equal(("Sample Mutual", "TEST_LIST", ScopeImportStatuses.Review), (view.Import.Carrier, view.Import.CarrierPriceList, view.Import.Status));
        Assert.Equal(3, view.Lines.Count); // blank description and negative quantity dropped
        var tearOff = view.Lines[0];
        Assert.Equal((62.50m, 60.00m), (tearOff.OurUnitPrice!.Value, tearOff.CarrierUnitPrice!.Value)); // matched by code + activity
        Assert.Equal(285.00m, view.Lines[1].OurUnitPrice);
        Assert.Null(view.Lines[2].PriceListItemId); // no ridge vent on our list
        Assert.Equal("LF", view.Lines[2].Unit);

        // Untick the shingles, fix the tear-off quantity.
        var added = await svc.ApplyAsync(importId, [0, 2], new Dictionary<int, decimal> { [0] = 26m }, "");
        Assert.Equal(2, added);
        var section = await db.EstimateSections.SingleAsync(x => x.EstimateId == w.Estimate && x.Name == "Carrier scope – Sample Mutual");
        var lines = await db.EstimateLines.Where(l => l.SectionId == section.Id).OrderBy(l => l.Sort).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal((LineKinds.PriceBook, 26m, 62.50m), (lines[0].Kind, lines[0].Qty, lines[0].UnitPrice)); // our price
        Assert.Equal((LineKinds.Misc, 40m, 9.50m), (lines[1].Kind, lines[1].Qty, lines[1].UnitPrice)); // carrier price, custom line
        Assert.Contains("RFG VENT", lines[1].Description);

        Assert.Equal(ScopeImportStatuses.Imported, (await svc.GetAsync(importId)).Import.Status);
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.ApplyAsync(importId, [1], new Dictionary<int, decimal>(), "")); // only once
        Assert.Single(await svc.ForEstimateAsync(w.Estimate));
    }

    [Fact]
    public async Task Lines_without_our_codes_match_by_wording_or_get_a_suggestion_to_accept()
    {
        var w = await WorldAsync();
        fx.Ai.Reset();
        fx.Ai.NextExtraction = ("""
            {"carrier":"Sample Mutual","lines":[
              {"category":"RFG","code":"DRPEG","description":"R&R Drip edge","quantity":200,"unit":"LF","unit_price":3.10},
              {"description":"Laminated shingles - w/out felt","quantity":30,"unit":"SQ","unit_price":250},
              {"description":"*Drip edge","quantity":180,"unit":"SF","unit_price":1},
              {"description":"Satellite dish detach","quantity":1,"unit":"EA","unit_price":40}
            ]}
            """, "end_turn");
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var svc = s.Get<ScopeImportService>();
        var lines = (await svc.GetAsync(await svc.ExtractAsync(w.Estimate, Pdf(), "scope2.pdf"))).Lines;

        // A code we don't have, but our wording, at another activity: taken, marked to check.
        Assert.Equal((ScopeMatchKinds.Activity, 2.35m, true), (lines[0].MatchKind, lines[0].OurUnitPrice, lines[0].NeedsCheck));
        Assert.Contains("Replace version", lines[0].MatchNote);
        // Two of three words shared: only suggested.
        Assert.Equal((null, "Laminated shingles", 285m), (lines[1].PriceListItemId, lines[1].SuggestedDescription, lines[1].SuggestedUnitPrice));
        Assert.Contains("67%", lines[1].MatchNote);
        // Our wording but a different unit: never taken by itself.
        Assert.Equal((null, "Drip edge", "LF"), (lines[2].PriceListItemId, lines[2].SuggestedDescription, lines[2].SuggestedUnit));
        Assert.Contains("carrier's line is in SF", lines[2].MatchNote);
        Assert.Equal((null, null, null), (lines[3].PriceListItemId, lines[3].SuggestedItemId, lines[3].MatchKind)); // nothing like it

        // Accept the shingle suggestion; the unit mismatch stays a custom line.
        var importId = (await svc.ForEstimateAsync(w.Estimate)).Single().Id;
        await svc.ApplyAsync(importId, [0, 1, 2], new Dictionary<int, decimal>(), "Supplement", useSuggestion: [1]);
        var db = s.Get<OesDbContext>();
        var section = await db.EstimateSections.SingleAsync(x => x.EstimateId == w.Estimate && x.Name == "Supplement");
        var added = await db.EstimateLines.Where(l => l.SectionId == section.Id).OrderBy(l => l.Sort).ToListAsync();
        Assert.Equal([(LineKinds.PriceBook, 2.35m), (LineKinds.PriceBook, 285m), (LineKinds.Misc, 1m)], added.Select(l => (l.Kind, l.UnitPrice)));

        // The review page marks the check and offers the suggestion.
        fx.Ai.Reset();
        fx.Ai.NextExtraction = ("""{"lines":[{"description":"Laminated shingles - w/out felt","quantity":30,"unit":"SQ","unit_price":250},{"category":"RFG","code":"DRPEG","description":"R&R Drip edge","quantity":200,"unit":"LF"}]}""", "end_turn");
        var again = await svc.ExtractAsync(w.Estimate, Pdf(), "scope3.pdf");
        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = w.Admin.Email!, ["Password"] = MySqlFixture.Password,
            ["__RequestVerificationToken"] = System.Text.RegularExpressions.Regex.Match(await web.GetStringAsync("/Account/Login"),
                "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value,
        }));
        await web.GetAsync("/Account/SelectTenant");
        var page = await web.GetStringAsync($"/Estimates/ScopeReview/{again}");
        Assert.Contains("name=\"UseSuggestion\" value=\"0\"", page);
        Assert.Contains("data-match-kind=\"activity\"", page);
        Assert.Contains("1 by wording or another activity, marked to check", page);
    }

    [Fact]
    public async Task Supplement_compare_pairs_items_and_totals_the_difference()
    {
        var w = await WorldAsync();
        fx.Ai.Reset();
        fx.Ai.NextExtraction = (ScopeJson, "end_turn");
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var svc = s.Get<ScopeImportService>();
        var db = s.Get<OesDbContext>();
        var importId = await svc.ExtractAsync(w.Estimate, Pdf(), "scope.pdf");
        // Tear-off at 26 SQ (our price), ridge vent as a custom line; the shingles replacement is left out.
        await svc.ApplyAsync(importId, [0, 2], new Dictionary<int, decimal> { [0] = 26m }, "");
        // Plus an item the carrier missed.
        var est = s.Get<EstimateService>();
        var listId = (await db.Estimates.SingleAsync(e => e.Id == w.Estimate)).PriceListId!.Value;
        var drip = (await s.Get<CatalogService>().ItemsAsync(listId, "DRIP")).Items.Single().PriceListItemId;
        var section = await db.EstimateSections.Where(x => x.EstimateId == w.Estimate).OrderBy(x => x.Sort).Select(x => x.Id).FirstAsync();
        await est.AddPriceBookLineAsync(w.Estimate, section, drip, 120);

        var c = await svc.CompareAsync(importId);
        Assert.Equal(4, c.Rows.Count);
        var tearOff = c.Rows.Single(r => r.Code == "RFG 300S" && r.Activity == Activities.Remove);
        Assert.Equal((CompareKind.Different, 25.5m, 26m, 60.00m, 62.50m, 95.00m),
            (tearOff.Kind, tearOff.CarrierQty!.Value, tearOff.OurQty!.Value, tearOff.CarrierUnitPrice!.Value, tearOff.OurUnitPrice!.Value, tearOff.Difference));
        Assert.Equal(CompareKind.CarrierOnly, c.Rows.Single(r => r.Code == "RFG 300S" && r.Activity == Activities.Replace).Kind);
        var vent = c.Rows.Single(r => r.Code == "RFG VENT");
        Assert.Equal((CompareKind.Same, "Ridge vent"), (vent.Kind, vent.Description)); // the custom line pairs back with the carrier's line
        Assert.Equal((CompareKind.OursOnly, 282.00m), (c.Rows.Single(r => r.Code == "RFG DRIP").Kind, c.Rows.Single(r => r.Code == "RFG DRIP").Difference));
        Assert.Equal((9559.10m, 2287.00m, -7272.10m), (c.CarrierTotal, c.OurTotal, c.Difference));
        Assert.Equal(CompareKind.Same, c.Rows[^1].Kind); // differences first
    }

    [Fact]
    public async Task Refusals_discards_and_other_tenants_are_handled()
    {
        var w = await WorldAsync();
        var other = await WorldAsync();
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var svc = s.Get<ScopeImportService>();

        fx.Ai.Reset();
        fx.Ai.NextExtraction = (null, "refusal");
        Assert.Contains("by hand", (await Assert.ThrowsAsync<CrmRuleException>(() => svc.ExtractAsync(w.Estimate, Pdf(), "scope.pdf"))).Message);
        Assert.Empty(await svc.ForEstimateAsync(w.Estimate));

        fx.Ai.Reset();
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.ExtractAsync(w.Estimate, Pdf(), "scope.xlsx"));
        Assert.Empty(fx.Ai.Extractions);

        fx.Ai.NextExtraction = (ScopeJson, "end_turn");
        var importId = await svc.ExtractAsync(w.Estimate, Pdf(), "scope.pdf");
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.ApplyAsync(importId, [], new Dictionary<int, decimal>(), "")); // nothing ticked
        await svc.DiscardAsync(importId);
        Assert.Equal(ScopeImportStatuses.Discarded, (await svc.GetAsync(importId)).Import.Status);

        // A signed estimate: the scope can still be read and compared, but not added.
        await s.Get<OesDbContext>().Estimates.Where(e => e.Id == w.Estimate)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.Status, EstimateStatuses.Signed));
        await using (var signed = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var svc2 = signed.Get<ScopeImportService>();
            var afterSigning = await svc2.ExtractAsync(w.Estimate, Pdf(), "supplement.pdf");
            Assert.Equal(3, (await svc2.CompareAsync(afterSigning)).Rows.Count(r => r.Kind == CompareKind.CarrierOnly));
            Assert.Contains("signed", (await Assert.ThrowsAsync<CrmRuleException>(() => svc2.ApplyAsync(afterSigning, [0], new Dictionary<int, decimal>(), ""))).Message);
        }

        // Another tenant can't see the import.
        await using var o = fx.ScopeAs(other.Tenant, other.Admin);
        await Assert.ThrowsAsync<RecordNotFoundException>(() => o.Get<ScopeImportService>().GetAsync(importId));
    }
}
