using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class EstimatingTests(MySqlFixture fx)
{
    private const string SampleCsv =
        "category,code,description,unit,activity,unit_price,material\n" +
        "RFG,300S,Laminated shingles,SQ,replace,285.00,150\n" +
        "RFG,300S,Laminated shingles,SQ,remove,62.50,\n" +
        "RFG,DRIP,Drip edge,LF,replace,2.35,1.10\n" +
        "RFG,FELT,Synthetic underlayment,SQ,replace,38.00,20\n" +
        "SDG,VNL,Vinyl siding,SF,r&r,4.10,2\n";

    private sealed record World(Guid Tenant, AppUser Admin, Guid Opportunity, Guid PriceList, Guid Property);

    private async Task<World> WorldAsync(AppUser? owner = null)
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var catalog = s.Get<CatalogService>();
        var listId = await catalog.SaveListAsync(null, new PriceListInput { Name = "Test list", Status = PriceListStatuses.Active });
        await catalog.ImportCsvAsync(listId, new StringReader(SampleCsv));

        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "1 Estimate Way", City = "Lincoln", State = "NE", PostalCode = "68502" });
        var structure = (await s.Get<PropertyService>().GetAsync(property)).Structures.Single();
        var roofing = (await s.Get<MeasurementService>().TradesAsync()).Single(x => x.Code == "roofing");
        await s.Get<MeasurementService>().SaveVersionAsync(property, structure.Id, roofing.Id,
            new MeasurementInput { Values = new() { ["|total_area"] = 2550m, ["|eaves"] = 120m } });

        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Est", LastName = "Imate", OwnerUserId = admin.Id });
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property, OwnerUserId = admin.Id, IsInsurance = true });
        return new World(t.Id, admin, opp, listId, property);
    }

    private static async Task<Guid> PriceItemAsync(AsyncServiceScope s, Guid listId, string code, string activity) =>
        (await s.Get<CatalogService>().ItemsAsync(listId, code)).Items.Single(i => i.Code == code && i.Activity == activity).PriceListItemId;

    [Fact]
    public async Task Csv_import_creates_catalog_and_prices_and_reimport_updates()
    {
        var w = await WorldAsync();
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var catalog = s.Get<CatalogService>();
        var items = await catalog.ItemsAsync(w.PriceList, null);
        Assert.Equal(5, items.Total);
        Assert.Contains(items.Items, i => i.Code == "300S" && i.Activity == Activities.Remove && i.UnitPrice == 62.50m);
        Assert.Contains(items.Items, i => i.Code == "VNL" && i.Activity == Activities.RemoveReplace);

        var result = await catalog.ImportCsvAsync(w.PriceList, new StringReader(
            "category,code,description,unit,activity,unit_price\nRFG,300S,Laminated shingles,SQ,replace,299.00\nRFG,BAD,Broken,SQ,replace,abc\n"));
        Assert.Equal(1, result.PricesUpdated);
        Assert.Equal(0, result.ItemsCreated);
        Assert.Single(result.Errors);
        Assert.Contains("Line 3", result.Errors[0]);
        Assert.Equal(299.00m, (await catalog.ItemsAsync(w.PriceList, "300S")).Items.Single(i => i.Activity == Activities.Replace).UnitPrice);
    }

    [Fact]
    public async Task New_estimates_take_the_company_defaults()
    {
        var w = await WorldAsync();
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var settings = s.Get<Oes.Infrastructure.Admin.SettingsService>();
        await Assert.ThrowsAsync<CrmRuleException>(() => settings.SaveEstimateDefaultsAsync(new() { ValidDays = 0 }));
        await Assert.ThrowsAsync<CrmRuleException>(() => settings.SaveEstimateDefaultsAsync(new() { TaxRate = 31 }));
        await settings.SaveEstimateDefaultsAsync(new()
        {
            Terms = " 50% down, balance on completion. ", InsuranceTerms = "Deductible due at signing.", ValidDays = 45, TaxRate = 7.5m,
            InsuranceOverheadPct = 12, InsuranceProfitPct = 8,
        });
        Assert.Equal("50% down, balance on completion.", (await settings.EstimateDefaultsAsync()).Terms);

        var est = s.Get<EstimateService>();
        var insurance = (await est.GetAsync(await est.CreateAsync(new NewEstimateInput { OpportunityId = w.Opportunity, PriceListId = w.PriceList }))).Estimate;
        Assert.Equal(("Deductible due at signing.", 12m, 8m, 7.5m), (insurance.Terms, insurance.OverheadPct, insurance.ProfitPct, insurance.TaxRate));
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45)), insurance.ExpiresOn);

        var contact = (await s.Get<OpportunityService>().GetAsync(w.Opportunity)).Contact.Id;
        var retailOpp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, Name = "Gutters" });
        var retail = (await est.GetAsync(await est.CreateAsync(new NewEstimateInput { OpportunityId = retailOpp, PriceListId = w.PriceList }))).Estimate;
        Assert.Equal(("50% down, balance on completion.", 0m, 0m, 7.5m), (retail.Terms, retail.OverheadPct, retail.ProfitPct, retail.TaxRate));

        var rep = await fx.AddMemberAsync(w.Tenant, "Sales Rep");
        await using var r = fx.ScopeAs(w.Tenant, rep);
        await Assert.ThrowsAsync<RecordForbiddenException>(() => r.Get<Oes.Infrastructure.Admin.SettingsService>().SaveEstimateDefaultsAsync(new()));
    }

    [Fact]
    public async Task Sections_come_from_measurements_and_prices_are_copied_onto_lines()
    {
        var w = await WorldAsync();
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var est = s.Get<EstimateService>();
        var id = await est.CreateAsync(new NewEstimateInput { OpportunityId = w.Opportunity, PriceListId = w.PriceList });
        var d = await est.GetAsync(id);
        var section = Assert.Single(d.Sections);
        Assert.Equal("Main House – Roofing", section.Section.Name);
        Assert.Contains(section.Measurements, m => m.Unit == "SQ" && m.Qty == 25.5m);
        Assert.Equal(10m, d.Estimate.OverheadPct); // insurance default O&P

        await est.AddPriceBookLineAsync(id, section.Section.Id, await PriceItemAsync(s, w.PriceList, "300S", Activities.Replace), 25.5m);

        // Changing the price list afterwards doesn't touch the line.
        await s.Get<CatalogService>().SetPriceAsync(await PriceItemAsync(s, w.PriceList, "300S", Activities.Replace), 999m);
        var line = (await est.GetAsync(id)).Sections.Single().Lines.Single();
        Assert.Equal(285.00m, line.UnitPrice);
        Assert.Equal("RFG 300S", line.Code);
        Assert.Equal(7267.50m, line.Amount);
        Assert.True(line.IsMaterial);

        var e = (await est.GetAsync(id)).Estimate;
        Assert.Equal(7267.50m, e.Subtotal);
        Assert.Equal(e.Subtotal + e.Overhead + e.Profit + e.Tax, e.Total);
    }

    [Fact]
    public async Task Price_exceptions_need_someone_else_to_approve_before_issue()
    {
        var w = await WorldAsync();
        var manager = await fx.AddMemberAsync(w.Tenant, "Sales Manager");
        Guid id, below, misc;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var est = s.Get<EstimateService>();
            id = await est.CreateAsync(new NewEstimateInput { OpportunityId = w.Opportunity, PriceListId = w.PriceList });
            var section = (await est.GetAsync(id)).Sections.Single().Section.Id;
            below = await est.AddPriceBookLineAsync(id, section, await PriceItemAsync(s, w.PriceList, "DRIP", Activities.Replace), 120);
            await est.UpdateLineAsync(below, 120, 1.99m, null, null);
            misc = await est.AddMiscLineAsync(id, section, "Satellite dish detach & reset", 1, "EA", 125m, false);

            var d = await est.GetAsync(id);
            Assert.Equal(2, d.PendingApprovals);
            await Assert.ThrowsAsync<CrmRuleException>(() => est.IssueAsync(id, null));
            // The admin requested both exceptions, so can't approve them.
            await Assert.ThrowsAsync<RecordForbiddenException>(() => est.DecideAsync(below, true, null));
        }
        await using (var s = fx.ScopeAs(w.Tenant, manager))
        {
            var est = s.Get<EstimateService>();
            Assert.Equal(2, (await est.PendingApprovalsAsync()).Count(a => a.EstimateId == id));
            await Assert.ThrowsAsync<CrmRuleException>(() => est.DecideAsync(misc, false, null)); // rejecting needs a reason
            await est.DecideAsync(below, true, "Matches carrier price");
            await est.DecideAsync(misc, true, null);
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var versionId = await s.Get<EstimateService>().IssueAsync(id, null);
            Assert.NotEqual(Guid.Empty, versionId);
        }
    }

    [Fact]
    public async Task Issued_versions_are_frozen_and_revisions_need_a_reason()
    {
        var w = await WorldAsync();
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var est = s.Get<EstimateService>();
        var id = await est.CreateAsync(new NewEstimateInput { OpportunityId = w.Opportunity, PriceListId = w.PriceList });
        var section = (await est.GetAsync(id)).Sections.Single().Section.Id;
        var lineId = await est.AddPriceBookLineAsync(id, section, await PriceItemAsync(s, w.PriceList, "300S", Activities.Replace), 25.5m);
        await est.UpdateSettingsAsync(id, new EstimateSettingsInput { Name = "Roof", Terms = "Net 30", OverheadPct = 10, ProfitPct = 10 });

        await Assert.ThrowsAsync<RecordNotFoundException>(() => est.IssueAsync(Guid.Empty, null));
        var v1 = await est.IssueAsync(id, null);
        var (version, content, _) = await est.GetVersionAsync(v1);
        Assert.Equal(1, version.VersionNo);
        Assert.Equal("Net 30", content.Terms);
        Assert.Null(content.CustomerAddress); // no billing or mailing address of their own
        Assert.Equal(25.5m, content.Sections.Single().Lines.Single().Qty);
        Assert.Equal(64, version.ContentHash.Length);

        // Editing after issue starts a revision; v1 stays exactly as issued.
        await est.UpdateLineAsync(lineId, 28m, 285m, null, null);
        Assert.Equal(EstimateStatuses.Draft, (await est.GetAsync(id)).Estimate.Status);
        Assert.Equal(25.5m, (await est.GetVersionAsync(v1)).Content.Sections.Single().Lines.Single().Qty);

        await Assert.ThrowsAsync<CrmRuleException>(() => est.IssueAsync(id, null));
        var customer = (await est.GetAsync(id)).Contact.Id;
        await s.Get<Oes.Infrastructure.Crm.ContactService>().SaveAddressAsync(customer, Oes.Domain.Crm.ContactAddressKinds.Billing,
            new Oes.Infrastructure.Crm.AddressInput { Line1 = "PO Box 7", City = "Omaha", State = "NE", PostalCode = "68102" });
        var v2 = await est.IssueAsync(id, "Added starter course");
        Assert.Equal("PO Box 7, Omaha, NE 68102", (await est.GetVersionAsync(v2)).Content.CustomerAddress);
        var versions = (await est.GetAsync(id)).Versions;
        Assert.Equal(VersionStatuses.Superseded, versions.Single(v => v.Id == v1).Status);
        Assert.Equal(VersionStatuses.Issued, versions.Single(v => v.Id == v2).Status);

        // Only the latest issued version can be signed.
        await Assert.ThrowsAsync<CrmRuleException>(() => est.MarkSignedAsync(v1, "Est Imate"));
    }

    [Fact]
    public async Task Signing_wins_the_opportunity_and_locks_the_estimate()
    {
        var w = await WorldAsync();
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        var est = s.Get<EstimateService>();
        var id = await est.CreateAsync(new NewEstimateInput { OpportunityId = w.Opportunity, PriceListId = w.PriceList });
        var section = (await est.GetAsync(id)).Sections.Single().Section.Id;
        var lineId = await est.AddPriceBookLineAsync(id, section, await PriceItemAsync(s, w.PriceList, "FELT", Activities.Replace), 25.5m);
        var v1 = await est.IssueAsync(id, null);
        await est.MarkSignedAsync(v1, "Est Imate");

        var d = await est.GetAsync(id);
        Assert.Equal(EstimateStatuses.Signed, d.Estimate.Status);
        Assert.False(d.CanEdit);
        await Assert.ThrowsAsync<CrmRuleException>(() => est.UpdateLineAsync(lineId, 30, 38, null, null));

        var opp = (await s.Get<OpportunityService>().GetAsync(w.Opportunity)).Opportunity;
        Assert.Equal(Outcomes.Won, opp.Outcome);
        Assert.Equal(d.Estimate.Total, opp.ExpectedValue);
    }

    [Fact]
    public async Task Line_item_estimates_use_products_and_reps_cannot_see_others_estimates()
    {
        var w = await WorldAsync();
        var rep = await fx.AddMemberAsync(w.Tenant, "Sales Rep");
        Guid id;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var product = await s.Get<CatalogService>().SaveProductAsync(null, new ProductInput { Sku = "gut-k5", Name = "5\" K-style gutter", Unit = "LF", DefaultPrice = 9.5m, DefaultCost = 4m });
            var est = s.Get<EstimateService>();
            id = await est.CreateAsync(new NewEstimateInput { OpportunityId = w.Opportunity, Method = EstimateMethods.LineItem });
            var section = (await est.GetAsync(id)).Sections.Single().Section.Id;
            await est.AddProductLineAsync(id, section, product, 120);
            var line = (await est.GetAsync(id)).Sections.Single().Lines.Single();
            Assert.Equal("GUT-K5", line.Code);
            Assert.Equal(4m, line.UnitCost);
            Assert.Equal(ApprovalStatuses.None, line.ApprovalStatus);
            await Assert.ThrowsAsync<CrmRuleException>(() => est.AddPriceBookLineAsync(id, section, Guid.NewGuid(), 1));
        }
        await using (var s = fx.ScopeAs(w.Tenant, rep))
        {
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<EstimateService>().GetAsync(id));
            Assert.DoesNotContain(await s.Get<EstimateService>().ListAsync(null, null, false), e => e.Id == id);
        }
    }
}
