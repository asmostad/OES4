using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Production;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Production;
using Oes.Infrastructure.Persistence;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class ProductionTests(MySqlFixture fx)
{
    private sealed record Sold(Guid Tenant, AppUser Admin, Guid Estimate, Guid Opportunity, decimal Total);

    /// <summary>A signed line-item estimate with a roofing and a gutter section.</summary>
    private async Task<Sold> SignedEstimateAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "9 Production Pl", City = "Lincoln", State = "NE", PostalCode = "68502" });
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Pro", LastName = "Duction", OwnerUserId = admin.Id });
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property, OwnerUserId = admin.Id });
        var trades = await s.Get<MeasurementService>().TradesAsync();
        var est = s.Get<EstimateService>();
        var id = await est.CreateAsync(new NewEstimateInput { OpportunityId = opp, Method = EstimateMethods.LineItem });
        var structure = (await s.Get<PropertyService>().GetAsync(property)).Structures.Single().Id;
        await est.AddSectionAsync(id, "Roof", structure, trades.Single(x => x.Code == "roofing").Id);
        await est.AddSectionAsync(id, "Gutters", structure, trades.Single(x => x.Code == "gutters").Id);
        var sections = (await est.GetAsync(id)).Sections;
        var product = await s.Get<CatalogService>().SaveProductAsync(null, new ProductInput { Sku = "ROOF-SQ", Name = "Roof per square", Unit = "SQ", DefaultPrice = 400m });
        var gutter = await s.Get<CatalogService>().SaveProductAsync(null, new ProductInput { Sku = "GUT", Name = "Gutter", Unit = "LF", DefaultPrice = 10m });
        await est.AddProductLineAsync(id, sections.Single(x => x.Section.Name == "Roof").Section.Id, product, 25);
        await est.AddProductLineAsync(id, sections.Single(x => x.Section.Name == "Gutters").Section.Id, gutter, 120);
        var v = await est.IssueAsync(id, null);
        await est.MarkSignedAsync(v, "Pro Duction");
        return new Sold(t.Id, admin, id, opp, (await est.GetAsync(id)).Estimate.Total);
    }

    [Fact]
    public async Task Signed_estimate_becomes_a_project_with_one_trade_per_estimate_trade()
    {
        var sold = await SignedEstimateAsync();
        await using var s = fx.ScopeAs(sold.Tenant, sold.Admin);
        var svc = s.Get<ProjectService>();
        var id = await svc.CreateFromEstimateAsync(sold.Estimate);
        var d = await svc.GetAsync(id);

        Assert.StartsWith($"P-{DateTime.UtcNow.Year}-", d.Project.Number);
        Assert.Equal(sold.Total, d.Project.ContractValue);
        Assert.Equal(StageRoles.Initial, d.Stage.Role);
        Assert.Equal(2, d.Trades.Count);
        Assert.Equal(10000m, d.Trades.Single(t => t.Type.Code == "roofing").Trade.Amount);
        Assert.Equal(1200m, d.Trades.Single(t => t.Type.Code == "gutters").Trade.Amount);
        Assert.All(d.Trades, t => Assert.Equal(StageRoles.Initial, t.Stage.Role));

        await Assert.ThrowsAsync<CrmRuleException>(() => svc.CreateFromEstimateAsync(sold.Estimate)); // only one project per estimate
    }

    [Fact]
    public async Task Gates_need_dates_and_automation_moves_the_project_forward_only()
    {
        var sold = await SignedEstimateAsync();
        await using var s = fx.ScopeAs(sold.Tenant, sold.Admin);
        var svc = s.Get<ProjectService>();
        var id = await svc.CreateFromEstimateAsync(sold.Estimate);
        var d = await svc.GetAsync(id);
        var stage = (string role) => d.TradeStages.Single(x => x.Role == role).Id;
        var roof = d.Trades.Single(t => t.Type.Code == "roofing").Trade.Id;
        var gut = d.Trades.Single(t => t.Type.Code == "gutters").Trade.Id;

        // Materials ordered: no date needed, and the project starts production automatically.
        var preview = await svc.PreviewTradeMoveAsync(roof, stage(StageRoles.MaterialsOrdered));
        Assert.True(preview.Allowed);
        Assert.Single(preview.Automations);
        await svc.MoveTradeAsync(roof, stage(StageRoles.MaterialsOrdered), null);
        Assert.Equal(StageRoles.ProductionStart, (await svc.GetAsync(id)).Stage.Role);

        // Deliveries need a material date; work needs a work date.
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.MoveTradeAsync(roof, stage(StageRoles.MaterialsDelivered), null));
        await svc.UpdateTradeAsync(roof, null, new DateOnly(2026, 12, 1), null, null);
        await svc.MoveTradeAsync(roof, stage(StageRoles.MaterialsDelivered), null);
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.MoveTradeAsync(roof, stage(StageRoles.Labor), null));
        await svc.UpdateTradeAsync(roof, null, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 3), null);
        await svc.UpdateTradeAsync(gut, null, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 4), null);

        // The project can't be marked complete while a trade is open.
        var complete = d.ProjectStages.Single(x => x.Role == StageRoles.Complete).Id;
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.MoveProjectAsync(id, complete, null));

        // Finishing the last trade completes the project automatically.
        await svc.MoveTradeAsync(roof, stage(StageRoles.Complete), null);
        Assert.Equal(StageRoles.ProductionStart, (await svc.GetAsync(id)).Stage.Role);
        var last = await svc.MoveTradeAsync(gut, stage(StageRoles.Complete), null);
        Assert.Contains(last.Automations, a => a.StartsWith("All trades"));
        var done = await svc.GetAsync(id);
        Assert.Equal(StageRoles.Complete, done.Stage.Role);
        Assert.NotNull(done.Project.CompletedAt);
        Assert.Contains(done.History, h => h.IsAutomatic);

        // Moving a trade back and forward again doesn't drag the project backwards.
        await svc.MoveTradeAsync(gut, stage(StageRoles.Labor), null);
        Assert.Equal(StageRoles.Complete, (await svc.GetAsync(id)).Stage.Role);
    }

    [Fact]
    public async Task Change_orders_add_to_the_contract_only_when_approved()
    {
        var sold = await SignedEstimateAsync();
        await using var s = fx.ScopeAs(sold.Tenant, sold.Admin);
        var svc = s.Get<ProjectService>();
        var id = await svc.CreateFromEstimateAsync(sold.Estimate);
        var roof = (await svc.GetAsync(id)).Trades.Single(t => t.Type.Code == "roofing").Trade.Id;

        var co1 = await svc.AddChangeOrderAsync(id, roof, "Replace 3 sheets of rotted decking", 285m, "Found at tear-off", false);
        var co2 = await svc.AddChangeOrderAsync(id, null, "Skip gutter guards", -150m, null, false);
        Assert.Equal(sold.Total, (await svc.GetAsync(id)).Project.ContractValue);

        await Assert.ThrowsAsync<CrmRuleException>(() => svc.DecideChangeOrderAsync(co1, true, null)); // who approved?
        await svc.DecideChangeOrderAsync(co1, true, "Pro Duction");
        await svc.DecideChangeOrderAsync(co2, false, null);
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.DecideChangeOrderAsync(co1, true, "Again"));

        var d = await svc.GetAsync(id);
        Assert.Equal(sold.Total + 285m, d.Project.ContractValue);
        Assert.Equal(10285m, d.Trades.Single(t => t.Trade.Id == roof).Trade.Amount);
        Assert.Equal(new[] { 1, 2 }, d.ChangeOrders.Select(c => c.Sequence));
    }

    [Fact]
    public async Task Itemized_change_orders_total_their_lines_and_lock_once_decided()
    {
        var sold = await SignedEstimateAsync();
        await using var s = fx.ScopeAs(sold.Tenant, sold.Admin);
        var svc = s.Get<ProjectService>();
        var id = await svc.CreateFromEstimateAsync(sold.Estimate);
        var gutters = (await svc.GetAsync(id)).Trades.Single(t => t.Type.Code == "gutters").Trade.Id;
        var gutter = (await s.Get<CatalogService>().ProductsAsync("GUT", false)).Single().Id;

        var co = await svc.StartItemizedChangeOrderAsync(id, gutters, "Gutter changes", "Customer asked at walkthrough", false);
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.DecideChangeOrderAsync(co, true, "Pro Duction")); // nothing in it yet
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.AddChangeOrderPriceItemAsync(co, Guid.NewGuid(), 1)); // line-item job: no price list
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.AddChangeOrderCustomLineAsync(co, "Nothing", 0, null, 5));

        var more = await svc.AddChangeOrderProductAsync(co, gutter, 30);            // 30 LF x $10
        await svc.AddChangeOrderCustomLineAsync(co, "Downspout extension", 2, "ea", 45.5m); // 91
        var credit = await svc.AddChangeOrderCustomLineAsync(co, "No gutter guards", -1, null, 120m);
        var d = await svc.GetChangeOrderAsync(co);
        Assert.Equal((271m, true), (d.ChangeOrder.Amount, d.CanEdit));
        Assert.Equal(["GUT", null, null], d.Lines.Select(l => l.Code));
        Assert.Equal("EA", d.Lines[1].Unit);

        await svc.UpdateChangeOrderLineAsync(more, 40, 10m, null);
        await svc.RemoveChangeOrderLineAsync(credit);
        Assert.Equal(491m, (await svc.GetChangeOrderAsync(co)).ChangeOrder.Amount);

        await svc.DecideChangeOrderAsync(co, true, "Pro Duction");
        var after = await svc.GetAsync(id);
        Assert.Equal(sold.Total + 491m, after.Project.ContractValue);
        Assert.Equal(1200m + 491m, after.Trades.Single(t => t.Trade.Id == gutters).Trade.Amount);
        Assert.False((await svc.GetChangeOrderAsync(co)).CanEdit);
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.AddChangeOrderCustomLineAsync(co, "Late add", 1, null, 10));
        var single = await svc.AddChangeOrderAsync(id, null, "Lump sum", 100m, null, false);
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.AddChangeOrderCustomLineAsync(single, "Item", 1, null, 10));

        // The change order page and its printable copy.
        var web = fx.App.Browser();
        await web.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = sold.Admin.Email!, ["Password"] = MySqlFixture.Password,
            ["__RequestVerificationToken"] = System.Text.RegularExpressions.Regex.Match(await web.GetStringAsync("/Account/Login"),
                "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value,
        }));
        await web.GetAsync("/Account/SelectTenant");
        var page = await web.GetStringAsync($"/Projects/ChangeOrder/{co}");
        Assert.Contains("Downspout extension", page);
        Assert.Contains("Approved by Pro Duction", page);
        var print = await web.GetStringAsync($"/Projects/ChangeOrder/{co}?print=true");
        Assert.Contains("New contract total", print);
        var projectPage = await web.GetStringAsync($"/Projects/View/{id}");
        Assert.Contains($"/Projects/ChangeOrder/{co}", projectPage);
        var start = await web.PostAsync($"/Projects/View/{id}?handler=ChangeOrder", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["description"] = "Skylight", ["amount"] = "", ["tradeId"] = "",
            ["__RequestVerificationToken"] = System.Text.RegularExpressions.Regex.Match(projectPage, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value,
        }));
        Assert.StartsWith("/Projects/ChangeOrder/", start.Headers.Location!.OriginalString); // blank amount: itemized
    }

    [Fact]
    public async Task Removed_projects_leave_the_board_and_come_back()
    {
        var sold = await SignedEstimateAsync();
        await using var s = fx.ScopeAs(sold.Tenant, sold.Admin);
        var svc = s.Get<ProjectService>();
        var id = await svc.CreateFromEstimateAsync(sold.Estimate);
        await svc.RemoveFromBoardAsync(EntityTypes.Project, id, "waiting_insurance", "Mortgage company check");

        Assert.DoesNotContain((await svc.ProjectBoardAsync(null, false)).SelectMany(c => c.Cards), c => c.Id == id);
        Assert.Contains(await svc.ListAsync(null, null, true, false), r => r.Id == id && r.IsRemoved);
        var stage = (await svc.GetAsync(id)).ProjectStages[1].Id;
        await Assert.ThrowsAsync<CrmRuleException>(() => svc.MoveProjectAsync(id, stage, null));

        await svc.RestoreToBoardAsync(EntityTypes.Project, id);
        Assert.Contains((await svc.ProjectBoardAsync(null, false)).SelectMany(c => c.Cards), c => c.Id == id);
    }

    [Fact]
    public async Task Projects_follow_visibility_and_need_workflow_rights_to_move()
    {
        var sold = await SignedEstimateAsync();
        var rep = await fx.AddMemberAsync(sold.Tenant, "Sales Rep");
        Guid id;
        await using (var s = fx.ScopeAs(sold.Tenant, sold.Admin)) id = await s.Get<ProjectService>().CreateFromEstimateAsync(sold.Estimate);
        await using (var s = fx.ScopeAs(sold.Tenant, rep))
        {
            await Assert.ThrowsAsync<RecordNotFoundException>(() => s.Get<ProjectService>().GetAsync(id));
            Assert.DoesNotContain((await s.Get<ProjectService>().TradeBoardAsync(null, false)).SelectMany(c => c.Cards), c => c.ProjectId == id);
        }

        // A rep who owns the opportunity can see the project but can't advance the workflow (no workflow.advance).
        await using (var s = fx.ScopeAs(sold.Tenant, sold.Admin))
        {
            var db = s.Get<OesDbContext>();
            (await db.Opportunities.FirstAsync(o => o.Id == sold.Opportunity)).OwnerUserId = rep.Id;
            await db.SaveChangesAsync();
        }
        await using (var s = fx.ScopeAs(sold.Tenant, rep))
        {
            var d = await s.Get<ProjectService>().GetAsync(id);
            Assert.False(d.CanAdvance);
            await Assert.ThrowsAsync<RecordForbiddenException>(() => s.Get<ProjectService>().MoveTradeAsync(d.Trades[0].Trade.Id, d.TradeStages[1].Id, null));
        }
    }

    [Fact]
    public async Task Workflow_roles_are_unique_per_level()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        await using var s = fx.ScopeAs(t.Id, admin);
        var settings = s.Get<ListSettingsService>();
        await Assert.ThrowsAsync<CrmRuleException>(() =>
            settings.SaveWorkflowStageAsync(WorkflowLevels.Trade, null, "Another complete", StageRoles.Complete, "#10b981", null, true));
        await Assert.ThrowsAsync<CrmRuleException>(() =>
            settings.SaveWorkflowStageAsync(WorkflowLevels.Project, null, "Bad", StageRoles.Labor, "#10b981", null, true)); // trade-only role
        await settings.SaveWorkflowStageAsync(WorkflowLevels.Trade, null, "Permit pulled", "", "#64748b", 10, true);
        Assert.Contains(await settings.WorkflowStagesAsync(WorkflowLevels.Trade), x => x.Name == "Permit pulled");
    }
}
