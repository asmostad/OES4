using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Domain.Production;
using Oes.Domain.Tenancy;
using Oes.Domain.Work;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Infrastructure.Work;

public sealed class AutomationRuleInput
{
    public string Name { get; set; } = "";
    public string Trigger { get; set; } = AutomationTriggers.OpportunityStage;
    public Guid StageId { get; set; }
    public string Action { get; set; } = AutomationActions.CreateTask;
    public string? Text { get; set; }
    public int? DueInDays { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public Guid? SequenceId { get; set; }
}

public sealed record AutomationRuleRow(AutomationRule Rule, string StageName, string? SequenceName, string? AssigneeName, int Runs);
public sealed record AutomationRunRow(DateTime At, string RuleName, string EntityType, Guid EntityId, string Status, string? Detail);

/// <summary>Automation rules admin (automation.manage).</summary>
public sealed class AutomationService(OesDbContext db, RecordScope scope)
{
    public async Task<List<AutomationRuleRow>> ListAsync(CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.AutomationManage);
        var rules = await db.AutomationRules.OrderBy(r => r.Trigger).ThenBy(r => r.Name).ToListAsync(ct);
        var pipeline = await db.PipelineStages.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var workflow = await db.WorkflowStages.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var sequences = await db.Sequences.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var users = await db.Users.ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
        var runs = await db.AutomationRuns.GroupBy(r => r.RuleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return rules.Select(r => new AutomationRuleRow(r,
            (r.Trigger == AutomationTriggers.OpportunityStage ? pipeline.GetValueOrDefault(r.StageId) : workflow.GetValueOrDefault(r.StageId)) ?? "(deleted stage)",
            r.SequenceId is { } s ? sequences.GetValueOrDefault(s) : null, r.AssigneeUserId is { } u ? users.GetValueOrDefault(u) : null,
            runs.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<Guid> SaveAsync(Guid? id, AutomationRuleInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.AutomationManage);
        var name = input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 150) throw new CrmRuleException("Give the rule a name.");
        if (!AutomationTriggers.All.Contains(input.Trigger)) throw new CrmRuleException("Pick what starts the rule.");
        var stageOk = input.Trigger switch
        {
            AutomationTriggers.OpportunityStage => await db.PipelineStages.AnyAsync(s => s.Id == input.StageId, ct),
            AutomationTriggers.ProjectStage => await db.WorkflowStages.AnyAsync(s => s.Id == input.StageId && s.Level == WorkflowLevels.Project, ct),
            _ => await db.WorkflowStages.AnyAsync(s => s.Id == input.StageId && s.Level == WorkflowLevels.Trade, ct),
        };
        if (!stageOk) throw new CrmRuleException("Pick a stage.");
        if (!AutomationActions.All.Contains(input.Action)) throw new CrmRuleException("Pick what the rule does.");
        var text = input.Text?.Trim();
        if (input.Action is AutomationActions.CreateTask or AutomationActions.Notify)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 500) throw new CrmRuleException(input.Action == AutomationActions.CreateTask ? "Give the task a title." : "Write the notification.");
            if (MergeFields.Unknown(text) is { Count: > 0 } unknown) throw new CrmRuleException($"Unknown merge field: {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}.");
        }
        if (input.Action == AutomationActions.Enroll && (input.SequenceId is not { } sid || !await db.Sequences.AnyAsync(s => s.Id == sid, ct)))
            throw new CrmRuleException("Pick a nurture workflow.");
        if (input.DueInDays is < 0 or > 365) throw new CrmRuleException("Due in 0 to 365 days.");
        if (input.AssigneeUserId is { } a && !await db.Memberships.AnyAsync(m => m.UserId == a && m.Status == MembershipStatus.Active, ct))
            throw new CrmRuleException("Pick an active user, or leave it as the owner.");

        var r = id is { } existing
            ? await db.AutomationRules.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new RecordNotFoundException("Rule")
            : db.AutomationRules.Add(new AutomationRule { CreatedByUserId = scope.Me }).Entity;
        r.Name = name; r.Trigger = input.Trigger; r.StageId = input.StageId; r.Action = input.Action;
        r.Text = input.Action == AutomationActions.Enroll ? null : text;
        r.DueInDays = input.Action == AutomationActions.CreateTask ? input.DueInDays : null;
        r.AssigneeUserId = input.Action == AutomationActions.Enroll ? null : input.AssigneeUserId;
        r.SequenceId = input.Action == AutomationActions.Enroll ? input.SequenceId : null;
        await db.SaveChangesAsync(ct);
        return r.Id;
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.AutomationManage);
        var r = await db.AutomationRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Rule");
        r.IsActive = active;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.AutomationManage);
        var r = await db.AutomationRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Rule");
        db.AutomationRules.Remove(r);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AutomationRunRow>> RecentRunsAsync(int take = 50, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.AutomationManage);
        return await (from r in db.AutomationRuns
                      join rule in db.AutomationRules on r.RuleId equals rule.Id into rs from rule in rs.DefaultIfEmpty()
                      orderby r.CreatedAt descending
                      select new AutomationRunRow(r.CreatedAt, rule == null ? "(deleted rule)" : rule.Name, r.EntityType, r.EntityId, r.Status, r.Detail))
            .Take(take).ToListAsync(ct);
    }
}

/// <summary>
/// Applies automation rules to recent stage changes (every minute, from the worker). Reading the
/// stage-change history catches every path that moves a record: drag and drop, signing an estimate,
/// production automation. Each (rule, change) runs once (unique index); a rule only applies to moves
/// made after it was created, and only moves from the last two days are considered.
/// </summary>
public sealed class AutomationRunner(
    OesDbContext db, TenantContext tenant, IClock clock, TenantTime time, MergeFields merge, RecordItems items,
    NotificationService notifications, ILogger<AutomationRunner> log)
{
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(2);

    private sealed record Change(Guid Id, Guid StageId, string EntityType, Guid EntityId, DateTime At);
    private sealed record Target(string EntityType, Guid EntityId, Guid? OwnerUserId, Guid? ContactId, Guid? OpportunityId, string Label);

    public static async Task<int> RunAllAsync(IServiceScopeFactory scopes, CancellationToken ct = default)
    {
        List<Guid> tenants;
        await using (var s = scopes.CreateAsyncScope())
            tenants = await s.ServiceProvider.GetRequiredService<OesDbContext>().AutomationRules.IgnoreQueryFilters([OesDbContext.TenantFilter])
                .Where(r => r.IsActive).Select(r => r.TenantId).Distinct().ToListAsync(ct);
        var ran = 0;
        foreach (var t in tenants)
        {
            await using var s = scopes.CreateAsyncScope();
            try { ran += await s.ServiceProvider.GetRequiredService<AutomationRunner>().RunAsync(t, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                s.ServiceProvider.GetRequiredService<ILogger<AutomationRunner>>().LogError(ex, "Automation failed for tenant {TenantId}", t);
            }
        }
        return ran;
    }

    public async Task<int> RunAsync(Guid tenantId, CancellationToken ct = default)
    {
        tenant.SetTenant(tenantId);
        var since = clock.UtcNow - Lookback;
        var rules = await db.AutomationRules.Where(r => r.IsActive).ToListAsync(ct);
        if (rules.Count == 0) return 0;

        var oppStages = rules.Where(r => r.Trigger == AutomationTriggers.OpportunityStage).Select(r => r.StageId).ToList();
        var wfStages = rules.Where(r => r.Trigger != AutomationTriggers.OpportunityStage).Select(r => r.StageId).ToList();
        var changes = new List<Change>();
        if (oppStages.Count > 0)
            changes.AddRange((await db.OpportunityStageChanges.Where(c => c.CreatedAt >= since && oppStages.Contains(c.ToStageId))
                .Select(c => new { c.Id, c.ToStageId, c.OpportunityId, c.CreatedAt }).ToListAsync(ct))
                .Select(c => new Change(c.Id, c.ToStageId, EntityTypes.Opportunity, c.OpportunityId, c.CreatedAt)));
        if (wfStages.Count > 0)
            changes.AddRange((await db.WorkflowStageChanges.Where(c => c.CreatedAt >= since && wfStages.Contains(c.ToStageId))
                .Select(c => new { c.Id, c.ToStageId, c.EntityType, c.EntityId, c.CreatedAt }).ToListAsync(ct))
                .Select(c => new Change(c.Id, c.ToStageId, c.EntityType, c.EntityId, c.CreatedAt)));
        if (changes.Count == 0) return 0;

        var changeIds = changes.Select(c => c.Id).ToList();
        var done = (await db.AutomationRuns.Where(r => changeIds.Contains(r.StageChangeId)).Select(r => new { r.RuleId, r.StageChangeId }).ToListAsync(ct))
            .Select(r => (r.RuleId, r.StageChangeId)).ToHashSet();
        var ran = 0;
        foreach (var rule in rules)
        {
            var type = rule.Trigger switch
            {
                AutomationTriggers.OpportunityStage => EntityTypes.Opportunity,
                AutomationTriggers.ProjectStage => EntityTypes.Project,
                _ => EntityTypes.Trade,
            };
            foreach (var change in changes.Where(c => c.StageId == rule.StageId && c.EntityType == type && c.At >= rule.CreatedAt && !done.Contains((rule.Id, c.Id))))
            {
                // Claim first: the unique (rule, change) index makes a second runner's insert fail.
                var run = new AutomationRun { RuleId = rule.Id, StageChangeId = change.Id, EntityType = change.EntityType, EntityId = change.EntityId, Status = "running" };
                db.AutomationRuns.Add(run);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); continue; }

                try
                {
                    var target = await TargetAsync(change, ct);
                    (run.Status, run.Detail, run.ItemId) = target is null
                        ? ("skipped", "The record no longer exists.", null)
                        : rule.Action switch
                        {
                            AutomationActions.CreateTask => await TaskAsync(rule, target, ct),
                            AutomationActions.Notify => await NotifyAsync(rule, target, ct),
                            _ => await EnrollAsync(rule, target, ct),
                        };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Automation rule {RuleId} failed on change {ChangeId}", rule.Id, change.Id);
                    (run.Status, run.Detail) = ("failed", "Something went wrong running this rule.");
                }
                await db.SaveChangesAsync(ct);
                ran++;
            }
        }
        return ran;
    }

    private async Task<Target?> TargetAsync(Change c, CancellationToken ct)
    {
        switch (c.EntityType)
        {
            case EntityTypes.Opportunity:
                var o = await db.Opportunities.Where(x => x.Id == c.EntityId && x.ArchivedAt == null)
                    .Select(x => new { x.OwnerUserId, x.PrimaryContactId, x.Number }).FirstOrDefaultAsync(ct);
                return o is null ? null : new Target(EntityTypes.Opportunity, c.EntityId, o.OwnerUserId, o.PrimaryContactId, c.EntityId, o.Number);
            case EntityTypes.Project:
                var p = await (from x in db.Projects where x.Id == c.EntityId
                               join opp in db.Opportunities on x.OpportunityId equals opp.Id
                               select new { x.OwnerUserId, opp.PrimaryContactId, x.OpportunityId, x.Number }).FirstOrDefaultAsync(ct);
                return p is null ? null : new Target(EntityTypes.Project, c.EntityId, p.OwnerUserId, p.PrimaryContactId, p.OpportunityId, p.Number);
            default:
                var t = await (from tr in db.ProjectTrades where tr.Id == c.EntityId
                               join x in db.Projects on tr.ProjectId equals x.Id
                               join opp in db.Opportunities on x.OpportunityId equals opp.Id
                               join ty in db.TradeTypes on tr.TradeTypeId equals ty.Id
                               select new { x.OwnerUserId, opp.PrimaryContactId, x.OpportunityId, Label = x.Number + " " + ty.Name }).FirstOrDefaultAsync(ct);
                return t is null ? null : new Target(EntityTypes.Trade, c.EntityId, t.OwnerUserId, t.PrimaryContactId, t.OpportunityId, t.Label);
        }
    }

    private async Task<Guid?> RecipientAsync(AutomationRule rule, Target target, CancellationToken ct)
    {
        var who = rule.AssigneeUserId ?? target.OwnerUserId;
        if (who is not { } id) return null;
        return await db.Memberships.AnyAsync(m => m.UserId == id && m.Status == MembershipStatus.Active, ct) ? id : null;
    }

    private async Task<(string, string?, Guid?)> TaskAsync(AutomationRule rule, Target target, CancellationToken ct)
    {
        if (await RecipientAsync(rule, target, ct) is not { } assignee) return ("skipped", "No one to give the task to (no active owner).", null);
        var values = await merge.ForAsync(target.EntityType, target.EntityId, target.ContactId, assignee, ct);
        var title = MergeFields.Render(rule.Text, values);
        var task = new TaskItem
        {
            Title = title.Length <= 300 ? title : title[..300], AssignedToUserId = assignee, EntityType = target.EntityType, EntityId = target.EntityId,
            DueAt = rule.DueInDays is { } d ? await time.ToUtcAsync((await time.TodayAsync()).AddDays(d), new TimeOnly(12, 0)) : null,
            Notes = $"Created by the automation rule \"{rule.Name}\".",
        };
        db.Tasks.Add(task);
        await items.LinkItemAsync(ItemTypes.Task, task.Id, target.EntityType, target.EntityId, ct);
        notifications.Notify(assignee, "task.assigned", $"New task: {task.Title}", target.Label, RecordUrls.For(target.EntityType, target.EntityId) ?? "/Tasks");
        return ("done", $"Task for {target.Label}", task.Id);
    }

    private async Task<(string, string?, Guid?)> NotifyAsync(AutomationRule rule, Target target, CancellationToken ct)
    {
        if (await RecipientAsync(rule, target, ct) is not { } who) return ("skipped", "No one to notify (no active owner).", null);
        var values = await merge.ForAsync(target.EntityType, target.EntityId, target.ContactId, who, ct);
        notifications.Notify(who, "automation", MergeFields.Render(rule.Text, values), target.Label, RecordUrls.For(target.EntityType, target.EntityId));
        return ("done", $"Notified about {target.Label}", null);
    }

    private async Task<(string, string?, Guid?)> EnrollAsync(AutomationRule rule, Target target, CancellationToken ct)
    {
        if (target.ContactId is not { } contact) return ("skipped", "No contact to enroll.", null);
        var seq = await db.Sequences.Include(s => s.Steps).FirstOrDefaultAsync(s => s.Id == rule.SequenceId && s.IsActive, ct);
        if (seq is null || seq.Steps.Count == 0) return ("skipped", "The workflow is off or has no steps.", null);
        if (await db.Enrollments.AnyAsync(e => e.SequenceId == seq.Id && e.ContactId == contact && e.Status == EnrollmentStatuses.Active, ct))
            return ("skipped", "Already in this workflow.", null);
        var e = new Enrollment
        {
            SequenceId = seq.Id, ContactId = contact, OpportunityId = target.OpportunityId,
            NextRunAt = clock.UtcNow.AddMinutes(seq.Steps.OrderBy(s => s.Sort).First().DelayMinutes),
        };
        db.Enrollments.Add(e);
        return ("done", $"Enrolled {target.Label} in {seq.Name}", e.Id);
    }
}
