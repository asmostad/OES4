using Microsoft.EntityFrameworkCore;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Production;
using Oes.Domain.Work;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Production;
using Oes.Infrastructure.Work;

namespace Oes.Integration.Tests;

[Collection(DbCollection.Name)]
public sealed class AutomationTests(MySqlFixture fx)
{
    private sealed record World(Guid Tenant, AppUser Admin, AppUser Rep, Guid Opportunity, Guid Property, List<Guid> Stages);

    private async Task<World> WorldAsync()
    {
        var (t, admin) = await fx.CreateTenantAsync();
        var rep = await fx.AddMemberAsync(t.Id, "Sales Rep");
        await using var s = fx.ScopeAs(t.Id, admin);
        var property = await s.Get<PropertyService>().CreateAsync(new PropertyInput { Line1 = "3 Rule Rd", City = "Lincoln", State = "NE", PostalCode = "68502" });
        var contact = await s.Get<ContactService>().CreateAsync(new ContactInput { FirstName = "Pat", LastName = "Rules", OwnerUserId = rep.Id });
        var opp = await s.Get<OpportunityService>().CreateAsync(new OpportunityInput { PrimaryContactId = contact, PropertyId = property, OwnerUserId = rep.Id });
        var stages = await s.Get<OesDbContext>().PipelineStages.Where(p => p.Outcome == Outcomes.Open).OrderBy(p => p.Sort).Select(p => p.Id).ToListAsync();
        return new World(t.Id, admin, rep, opp, property, stages);
    }

    private async Task<Guid> RuleAsync(World w, AutomationRuleInput input)
    {
        await using var s = fx.ScopeAs(w.Tenant, w.Admin);
        return await s.Get<AutomationService>().SaveAsync(null, input);
    }

    private async Task<int> RunAsync(World w)
    {
        await using (fx.ScopeFor(null)) { }
        await using var s = fx.ScopeFor(null);
        return await s.Get<AutomationRunner>().RunAsync(w.Tenant);
    }

    private async Task MoveAsync(World w, Guid stage)
    {
        await using var s = fx.ScopeAs(w.Tenant, w.Rep);
        await s.Get<OpportunityService>().MoveStageAsync(w.Opportunity, stage, null);
    }

    [Fact]
    public async Task Opportunity_stage_rules_create_tasks_notify_and_enroll_once_per_move()
    {
        var w = await WorldAsync();
        Guid sequence;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            sequence = await s.Get<NurtureService>().SaveAsync(null, new SequenceInput { Name = "After contact", Category = MessagePurposes.Transactional });
            await s.Get<NurtureService>().AddStepAsync(sequence, new StepInput { Kind = StepKinds.Task, DelayDays = 3, Subject = "Check in" });
        }
        await RuleAsync(w, new AutomationRuleInput { Name = "Call task", Trigger = AutomationTriggers.OpportunityStage, StageId = w.Stages[1],
            Action = AutomationActions.CreateTask, Text = "Call {{contact.first_name}} about {{property.street}}", DueInDays = 1 });
        await RuleAsync(w, new AutomationRuleInput { Name = "Tell the boss", Trigger = AutomationTriggers.OpportunityStage, StageId = w.Stages[1],
            Action = AutomationActions.Notify, Text = "{{opportunity.number}} was contacted", AssigneeUserId = w.Admin.Id });
        await RuleAsync(w, new AutomationRuleInput { Name = "Nurture", Trigger = AutomationTriggers.OpportunityStage, StageId = w.Stages[1],
            Action = AutomationActions.Enroll, SequenceId = sequence });

        await MoveAsync(w, w.Stages[1]);
        Assert.Equal(3, await RunAsync(w));
        Assert.Equal(0, await RunAsync(w)); // once per move

        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var db = s.Get<OesDbContext>();
            var task = await db.Tasks.SingleAsync(t => t.EntityId == w.Opportunity);
            Assert.Equal(("Call Pat about 3 Rule Rd", w.Rep.Id), (task.Title, task.AssignedToUserId)); // the owner
            Assert.NotNull(task.DueAt);
            var number = await db.Opportunities.Where(o => o.Id == w.Opportunity).Select(o => o.Number).SingleAsync();
            Assert.True(await db.Notifications.AnyAsync(n => n.UserId == w.Admin.Id && n.Title == $"{number} was contacted"));
            var e = await db.Enrollments.SingleAsync(x => x.OpportunityId == w.Opportunity);
            Assert.Equal((sequence, EnrollmentStatuses.Active), (e.SequenceId, e.Status));
            Assert.Equal(3, await db.AutomationRuns.CountAsync(r => r.EntityId == w.Opportunity && r.Status == "done"));
        }

        // Moving away and back is a new move: the task rule runs again, the enroll rule skips (already enrolled).
        await MoveAsync(w, w.Stages[2]);
        await MoveAsync(w, w.Stages[1]);
        Assert.Equal(3, await RunAsync(w));
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var db = s.Get<OesDbContext>();
            Assert.Equal(2, await db.Tasks.CountAsync(t => t.EntityId == w.Opportunity));
            Assert.Equal(1, await db.Enrollments.CountAsync(x => x.OpportunityId == w.Opportunity));
            Assert.True(await db.AutomationRuns.AnyAsync(r => r.Status == "skipped" && r.Detail == "Already in this workflow."));
        }
    }

    [Fact]
    public async Task Rules_only_apply_to_moves_after_they_exist_and_while_on()
    {
        var w = await WorldAsync();
        await MoveAsync(w, w.Stages[1]);
        var rule = await RuleAsync(w, new AutomationRuleInput { Name = "Late rule", Trigger = AutomationTriggers.OpportunityStage, StageId = w.Stages[1],
            Action = AutomationActions.CreateTask, Text = "Too late" });
        Assert.Equal(0, await RunAsync(w)); // the move happened before the rule

        await using (var s = fx.ScopeAs(w.Tenant, w.Admin)) await s.Get<AutomationService>().SetActiveAsync(rule, false);
        await MoveAsync(w, w.Stages[2]);
        await MoveAsync(w, w.Stages[1]);
        Assert.Equal(0, await RunAsync(w)); // switched off

        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var a = s.Get<AutomationService>();
            await Assert.ThrowsAsync<CrmRuleException>(() => a.SaveAsync(null, new AutomationRuleInput { Name = "Bad", StageId = w.Stages[1], Action = AutomationActions.CreateTask, Text = "{{contact.nickname}}" }));
            await Assert.ThrowsAsync<CrmRuleException>(() => a.SaveAsync(null, new AutomationRuleInput { Name = "Bad", StageId = w.Stages[1], Action = AutomationActions.Enroll }));
            await Assert.ThrowsAsync<CrmRuleException>(() => a.SaveAsync(null, new AutomationRuleInput { Name = "Bad", Trigger = AutomationTriggers.ProjectStage, StageId = w.Stages[1], Action = AutomationActions.Notify, Text = "x" }));
        }
        await using (var s = fx.ScopeAs(w.Tenant, w.Rep))
            await Assert.ThrowsAsync<RecordForbiddenException>(() => s.Get<AutomationService>().ListAsync());
    }

    [Fact]
    public async Task Project_stage_rules_fire_when_a_project_is_created_from_a_signed_estimate()
    {
        var w = await WorldAsync();
        Guid startStage;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var db = s.Get<OesDbContext>();
            startStage = await db.WorkflowStages.Where(x => x.Level == WorkflowLevels.Project && x.Role == StageRoles.Initial).Select(x => x.Id).FirstAsync();
        }
        await RuleAsync(w, new AutomationRuleInput { Name = "Order materials", Trigger = AutomationTriggers.ProjectStage, StageId = startStage,
            Action = AutomationActions.CreateTask, Text = "Order materials for {{project.number}}", DueInDays = 0 });

        Guid projectId;
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var est = s.Get<EstimateService>();
            var id = await est.CreateAsync(new NewEstimateInput { OpportunityId = w.Opportunity, Method = EstimateMethods.LineItem });
            var structure = (await s.Get<PropertyService>().GetAsync(w.Property)).Structures.Single().Id;
            var roofing = (await s.Get<MeasurementService>().TradesAsync()).Single(x => x.Code == "roofing");
            await est.AddSectionAsync(id, "Roof", structure, roofing.Id);
            var product = await s.Get<CatalogService>().SaveProductAsync(null, new ProductInput { Sku = "R", Name = "Roof", Unit = "SQ", DefaultPrice = 400m });
            await est.AddProductLineAsync(id, (await est.GetAsync(id)).Sections.Single(x => x.Section.Name == "Roof").Section.Id, product, 20);
            await est.MarkSignedAsync(await est.IssueAsync(id, null), "Pat Rules");
            projectId = await s.Get<ProjectService>().CreateFromEstimateAsync(id);
        }
        Assert.True(await RunAsync(w) >= 1);
        await using (var s = fx.ScopeAs(w.Tenant, w.Admin))
        {
            var db = s.Get<OesDbContext>();
            var number = await db.Projects.Where(p => p.Id == projectId).Select(p => p.Number).SingleAsync();
            var task = await db.Tasks.SingleAsync(t => t.EntityType == EntityTypes.Project && t.EntityId == projectId);
            Assert.Equal($"Order materials for {number}", task.Title);
            Assert.Equal(w.Admin.Id, task.AssignedToUserId); // the project manager (the estimate's owner)
        }
    }
}
