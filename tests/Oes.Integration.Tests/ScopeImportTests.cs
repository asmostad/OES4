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

        // Another tenant can't see the import.
        await using var o = fx.ScopeAs(other.Tenant, other.Admin);
        await Assert.ThrowsAsync<RecordNotFoundException>(() => o.Get<ScopeImportService>().GetAsync(importId));
    }
}
