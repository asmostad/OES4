using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Measurements;
using Oes.Domain.Production;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Production;

public sealed record ProjectRow(Guid Id, string Number, string Name, Guid StageId, string StageName, string StageColor, string StageRole,
    string ContactName, string? Address, string? OwnerName, decimal ContractValue, int Trades, int TradesComplete, DateTime? StageChangedAt,
    bool IsStale, bool IsRemoved, string? RemovalReason);

public sealed record TradeRow(ProjectTrade Trade, TradeType Type, WorkflowStage Stage, bool IsStale);

public sealed record TradeCard(Guid Id, Guid ProjectId, string ProjectNumber, string ProjectName, string TradeName, string TradeColor, string TradeIcon,
    Guid StageId, string? Address, DateOnly? MaterialDate, DateOnly? WorkDate, decimal Amount, bool IsStale, bool IsRemoved);

public sealed record HistoryRow(DateTime At, string? From, string To, string? By, bool IsAutomatic, string? Note);

public sealed record ProjectDetail(
    Project Project, WorkflowStage Stage, Opportunity Opportunity, Contact Contact, Property? Property, string? EstimateNumber,
    string? OwnerName, List<TradeRow> Trades, List<ChangeOrder> ChangeOrders, List<WorkflowStage> ProjectStages,
    List<WorkflowStage> TradeStages, List<HistoryRow> History, bool CanEdit, bool CanAdvance);

public sealed record TradeDetail(ProjectTrade Trade, TradeType Type, WorkflowStage Stage, Project Project, Property? Property, Contact Contact,
    List<WorkflowStage> Stages, List<HistoryRow> History, List<ChangeOrder> ChangeOrders, bool CanEdit, bool CanAdvance);

/// <summary>What will happen if a card moves: a blocker (gate) or the automatic follow-ups.</summary>
public sealed record MovePreview(bool Allowed, string? Blocker, List<string> Automations);

public sealed record BoardColumn<T>(WorkflowStage Stage, List<T> Cards);

/// <summary>
/// Production: projects from signed estimates, trades, role-based stages with gates and
/// forward-only automation, Remove from Board, and change orders.
/// </summary>
public sealed class ProjectService(OesDbContext db, RecordScope scope, RecordItems items, INumberSequence numbers, IClock clock, ICurrentUser user)
{
    public Task<List<WorkflowStage>> StagesAsync(string level, bool activeOnly = true, CancellationToken ct = default) =>
        db.WorkflowStages.Where(s => s.Level == level && (!activeOnly || s.IsActive)).OrderBy(s => s.Sort).ToListAsync(ct);

    // ---------------- Create ----------------

    /// <summary>Creates the project for a signed estimate: one trade per estimate trade, contract = signed version total.</summary>
    public async Task<Guid> CreateFromEstimateAsync(Guid estimateId, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ProjectsCreate);
        var est = await (await scope.EstimatesAsync()).FirstOrDefaultAsync(e => e.Id == estimateId, ct) ?? throw new RecordNotFoundException("Estimate");
        if (est.Status != EstimateStatuses.Signed || est.SignedVersionId is not { } versionId)
            throw new CrmRuleException("Only a signed estimate can become a project.");
        if (await db.Projects.FirstOrDefaultAsync(p => p.EstimateId == estimateId, ct) is { } existing)
            throw new CrmRuleException($"Project {existing.Number} already exists for this estimate.");
        var version = await db.EstimateVersions.FirstAsync(v => v.Id == versionId, ct);

        var projectStages = await StagesAsync(WorkflowLevels.Project, true, ct);
        var tradeStages = await StagesAsync(WorkflowLevels.Trade, true, ct);
        var pStart = projectStages.FirstOrDefault(s => s.Role == StageRoles.Initial) ?? projectStages.FirstOrDefault()
                     ?? throw new CrmRuleException("Set up project workflow stages first.");
        var tStart = tradeStages.FirstOrDefault(s => s.Role == StageRoles.Initial) ?? tradeStages.FirstOrDefault()
                     ?? throw new CrmRuleException("Set up trade workflow stages first.");

        var now = clock.UtcNow;
        var project = new Project
        {
            Number = await numbers.NextAsync(NumberedKinds.Project, ct), Name = est.Name, OpportunityId = est.OpportunityId,
            PropertyId = est.PropertyId, EstimateId = est.Id, EstimateVersionId = versionId, ClaimId = est.ClaimId,
            OwnerUserId = est.OwnerUserId, StageId = pStart.Id, StageChangedAt = now, OriginalContract = version.Total,
        };
        db.Projects.Add(project);
        db.WorkflowStageChanges.Add(new WorkflowStageChange { EntityType = EntityTypes.Project, EntityId = project.Id, ToStageId = pStart.Id, ByUserId = scope.Me });

        // One trade per trade on the estimate; sections without a trade roll into "General".
        var sections = await db.EstimateSections.Where(s => s.EstimateId == estimateId).OrderBy(s => s.Sort).ToListAsync(ct);
        var lines = await db.EstimateLines.Where(l => l.EstimateId == estimateId).ToListAsync(ct);
        var general = await db.TradeTypes.FirstOrDefaultAsync(t => t.Code == "general", ct);
        var sort = 0;
        foreach (var group in sections.GroupBy(s => s.TradeTypeId ?? general?.Id))
        {
            if (group.Key is not { } tradeTypeId) continue;
            var sectionIds = group.Select(s => s.Id).ToHashSet();
            // Empty sections (e.g. an unused default "Scope of work") don't become trades.
            if (!lines.Any(l => sectionIds.Contains(l.SectionId))) continue;
            var amount = lines.Where(l => sectionIds.Contains(l.SectionId)).Sum(l => l.Amount);
            var trade = new ProjectTrade
            {
                ProjectId = project.Id, TradeTypeId = tradeTypeId, StageId = tStart.Id, StageChangedAt = now, Amount = amount, Sort = sort += 10,
                Description = string.Join("; ", group.Select(s => s.Name)),
            };
            db.ProjectTrades.Add(trade);
            db.WorkflowStageChanges.Add(new WorkflowStageChange { EntityType = EntityTypes.Trade, EntityId = trade.Id, ToStageId = tStart.Id, ByUserId = scope.Me });
        }

        await items.LogAsync(EntityTypes.Project, project.Id, "project.created", $"Project {project.Number} created from {est.Number} v{version.VersionNo} · {version.Total:C2}", ct);
        await db.SaveChangesAsync(ct);
        return project.Id;
    }

    public async Task<Guid?> ForEstimateAsync(Guid estimateId, CancellationToken ct = default) =>
        await (await scope.ProjectsAsync()).Where(p => p.EstimateId == estimateId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);

    // ---------------- Read ----------------

    public async Task<List<ProjectRow>> ListAsync(string? q, Guid? stageId, bool includeRemoved, bool mine, Guid? opportunityId = null, CancellationToken ct = default)
    {
        var projects = await scope.ProjectsAsync();
        if (opportunityId is { } oid) projects = projects.Where(p => p.OpportunityId == oid);
        if (stageId is { } sid) projects = projects.Where(p => p.StageId == sid);
        if (!includeRemoved) projects = projects.Where(p => p.RemovedFromBoardAt == null);
        if (mine) projects = projects.Where(p => p.OwnerUserId == scope.Me);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            projects = projects.Where(p => p.Number.Contains(t) || p.Name.Contains(t)
                || db.Opportunities.Any(o => o.Id == p.OpportunityId && db.Contacts.Any(c => c.Id == o.PrimaryContactId && c.DisplayName.Contains(t)))
                || db.Properties.Any(x => x.Id == p.PropertyId && (x.Line1.Contains(t) || x.City.Contains(t))));
        }
        var now = clock.UtcNow;
        var raw = await (from p in projects
                         join s in db.WorkflowStages on p.StageId equals s.Id
                         join o in db.Opportunities on p.OpportunityId equals o.Id
                         join c in db.Contacts on o.PrimaryContactId equals c.Id
                         join pr in db.Properties on p.PropertyId equals pr.Id into ps from pr in ps.DefaultIfEmpty()
                         join u in db.Users on p.OwnerUserId equals u.Id into us from u in us.DefaultIfEmpty()
                         orderby p.CreatedAt descending
                         select new
                         {
                             p, s.Name, s.Color, s.Role, s.StaleAfterDays, c.DisplayName, pr, Owner = u == null ? null : u.FirstName + " " + u.LastName,
                             Trades = db.ProjectTrades.Count(t => t.ProjectId == p.Id),
                             Done = db.ProjectTrades.Count(t => t.ProjectId == p.Id && t.CompletedAt != null),
                         }).Take(500).ToListAsync(ct);
        return raw.Select(r => new ProjectRow(r.p.Id, r.p.Number, r.p.Name, r.p.StageId, r.Name, r.Color, r.Role, r.DisplayName, r.pr?.SingleLine,
            r.Owner, r.p.ContractValue, r.Trades, r.Done, r.p.StageChangedAt,
            r.StaleAfterDays is { } d && r.p.RemovedFromBoardAt == null && (now - (r.p.StageChangedAt ?? r.p.CreatedAt)).TotalDays > d,
            r.p.RemovedFromBoardAt != null, r.p.RemovalReasonKey)).ToList();
    }

    public async Task<List<BoardColumn<ProjectRow>>> ProjectBoardAsync(string? q, bool mine, CancellationToken ct = default)
    {
        var stages = await StagesAsync(WorkflowLevels.Project, true, ct);
        var rows = await ListAsync(q, null, false, mine, null, ct);
        return stages.Select(s => new BoardColumn<ProjectRow>(s, rows.Where(r => r.StageId == s.Id).ToList())).ToList();
    }

    /// <summary>The production board: every trade card across visible projects, by trade stage.</summary>
    public async Task<List<BoardColumn<TradeCard>>> TradeBoardAsync(Guid? tradeTypeId, bool mine, CancellationToken ct = default)
    {
        var stages = await StagesAsync(WorkflowLevels.Trade, true, ct);
        var projects = (await scope.ProjectsAsync()).Where(p => p.RemovedFromBoardAt == null);
        if (mine) projects = projects.Where(p => p.OwnerUserId == scope.Me);
        var trades = db.ProjectTrades.Where(t => t.RemovedFromBoardAt == null);
        if (tradeTypeId is { } tt) trades = trades.Where(t => t.TradeTypeId == tt);
        var now = clock.UtcNow;
        var raw = await (from t in trades
                         join p in projects on t.ProjectId equals p.Id
                         join ty in db.TradeTypes on t.TradeTypeId equals ty.Id
                         join s in db.WorkflowStages on t.StageId equals s.Id
                         join pr in db.Properties on p.PropertyId equals pr.Id into ps from pr in ps.DefaultIfEmpty()
                         orderby t.WorkDate ?? DateOnly.MaxValue, p.Number
                         select new { t, p.Number, ProjectName = p.Name, ty, s.StaleAfterDays, pr }).Take(800).ToListAsync(ct);
        var cards = raw.Select(r => new TradeCard(r.t.Id, r.t.ProjectId, r.Number, r.ProjectName, r.ty.Name, r.ty.Color, r.ty.Icon, r.t.StageId,
            r.pr?.SingleLine, r.t.MaterialDate, r.t.WorkDate, r.t.Amount,
            r.StaleAfterDays is { } d && (now - (r.t.StageChangedAt ?? r.t.CreatedAt)).TotalDays > d, false)).ToList();
        return stages.Select(s => new BoardColumn<TradeCard>(s, cards.Where(c => c.StageId == s.Id).ToList())).ToList();
    }

    public async Task<ProjectDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await (await scope.ProjectsAsync(includeArchived: true)).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Project");
        var opp = await db.Opportunities.FirstAsync(o => o.Id == p.OpportunityId, ct);
        var contact = await db.Contacts.FirstAsync(c => c.Id == opp.PrimaryContactId, ct);
        var property = p.PropertyId is null ? null : await db.Properties.FirstOrDefaultAsync(x => x.Id == p.PropertyId, ct);
        var estNumber = p.EstimateId is null ? null : await db.Estimates.Where(e => e.Id == p.EstimateId).Select(e => e.Number).FirstOrDefaultAsync(ct);
        var owner = p.OwnerUserId is null ? null : await db.Users.Where(u => u.Id == p.OwnerUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);
        var projectStages = await StagesAsync(WorkflowLevels.Project, false, ct);
        var tradeStages = await StagesAsync(WorkflowLevels.Trade, false, ct);
        var trades = await db.ProjectTrades.Where(t => t.ProjectId == id).OrderBy(t => t.Sort).ToListAsync(ct);
        var typeIds = trades.Select(t => t.TradeTypeId).ToList();
        var types = await db.TradeTypes.Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var now = clock.UtcNow;
        var tradeRows = trades.Select(t =>
        {
            var st = tradeStages.First(s => s.Id == t.StageId);
            return new TradeRow(t, types[t.TradeTypeId], st, st.StaleAfterDays is { } d && (now - (t.StageChangedAt ?? t.CreatedAt)).TotalDays > d);
        }).ToList();
        var cos = await db.ChangeOrders.Where(c => c.ProjectId == id).OrderBy(c => c.Sequence).ToListAsync(ct);
        return new ProjectDetail(p, projectStages.First(s => s.Id == p.StageId), opp, contact, property, estNumber, owner, tradeRows, cos,
            projectStages.Where(s => s.IsActive).ToList(), tradeStages.Where(s => s.IsActive).ToList(), await HistoryAsync(EntityTypes.Project, id, ct),
            await CanEditAsync(p), await CanAdvanceAsync(p));
    }

    public async Task<TradeDetail> GetTradeAsync(Guid tradeId, CancellationToken ct = default)
    {
        var t = await db.ProjectTrades.FirstOrDefaultAsync(x => x.Id == tradeId, ct) ?? throw new RecordNotFoundException("Trade");
        var p = await (await scope.ProjectsAsync(true)).FirstOrDefaultAsync(x => x.Id == t.ProjectId, ct) ?? throw new RecordNotFoundException("Trade");
        var type = await db.TradeTypes.FirstAsync(x => x.Id == t.TradeTypeId, ct);
        var stages = await StagesAsync(WorkflowLevels.Trade, false, ct);
        var opp = await db.Opportunities.FirstAsync(o => o.Id == p.OpportunityId, ct);
        var contact = await db.Contacts.FirstAsync(c => c.Id == opp.PrimaryContactId, ct);
        var property = p.PropertyId is null ? null : await db.Properties.FirstOrDefaultAsync(x => x.Id == p.PropertyId, ct);
        var cos = await db.ChangeOrders.Where(c => c.ProjectTradeId == tradeId).OrderBy(c => c.Sequence).ToListAsync(ct);
        return new TradeDetail(t, type, stages.First(s => s.Id == t.StageId), p, property, contact, stages.Where(s => s.IsActive).ToList(),
            await HistoryAsync(EntityTypes.Trade, tradeId, ct), cos, await CanEditAsync(p), await CanAdvanceAsync(p));
    }

    private Task<List<HistoryRow>> HistoryAsync(string entityType, Guid id, CancellationToken ct) =>
        (from h in db.WorkflowStageChanges where h.EntityType == entityType && h.EntityId == id
         join to in db.WorkflowStages on h.ToStageId equals to.Id
         join fromStage in db.WorkflowStages on h.FromStageId equals fromStage.Id into fs from fromStage in fs.DefaultIfEmpty()
         join u in db.Users on h.ByUserId equals u.Id into us from u in us.DefaultIfEmpty()
         orderby h.CreatedAt descending
         select new HistoryRow(h.CreatedAt, fromStage == null ? null : fromStage.Name, to.Name, u == null ? null : u.FirstName + " " + u.LastName, h.IsAutomatic, h.Note))
        .ToListAsync(ct);

    // ---------------- Rights ----------------

    private async Task<bool> CanEditAsync(Project p)
    {
        if (p.ArchivedAt is not null) return false;
        if (await user.HasPermissionAsync(Permissions.ProjectsEditAll)) return true;
        return await user.HasPermissionAsync(Permissions.ProjectsEditOwn) && (p.OwnerUserId is null || p.OwnerUserId == scope.Me);
    }

    private async Task<bool> CanAdvanceAsync(Project p) =>
        p.ArchivedAt is null && await user.HasPermissionAsync(Permissions.WorkflowAdvance) && await scope.CanSeeAsync(EntityTypes.Project, p.Id);

    private async Task<Project> ProjectForEditAsync(Guid id, CancellationToken ct)
    {
        var p = await (await scope.ProjectsAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Project");
        if (!await CanEditAsync(p)) throw new RecordForbiddenException("You can't edit this project.");
        return p;
    }

    private async Task<(ProjectTrade Trade, Project Project)> TradeForAsync(Guid tradeId, bool advance, CancellationToken ct)
    {
        var t = await db.ProjectTrades.FirstOrDefaultAsync(x => x.Id == tradeId, ct) ?? throw new RecordNotFoundException("Trade");
        var p = await (await scope.ProjectsAsync()).FirstOrDefaultAsync(x => x.Id == t.ProjectId, ct) ?? throw new RecordNotFoundException("Trade");
        if (advance ? !await CanAdvanceAsync(p) : !await CanEditAsync(p))
            throw new RecordForbiddenException(advance ? "You don't have permission to move work through the workflow." : "You can't edit this project.");
        return (t, p);
    }

    // ---------------- Stage moves, gates and automation ----------------

    /// <summary>Explains what moving a trade would do, without doing it (used by the board and trade page).</summary>
    public async Task<MovePreview> PreviewTradeMoveAsync(Guid tradeId, Guid stageId, CancellationToken ct = default)
    {
        var (trade, project) = await TradeForAsync(tradeId, advance: true, ct);
        return await PlanTradeMoveAsync(trade, project, stageId, ct);
    }

    private async Task<MovePreview> PlanTradeMoveAsync(ProjectTrade trade, Project project, Guid stageId, CancellationToken ct)
    {
        var tradeStages = await StagesAsync(WorkflowLevels.Trade, true, ct);
        var to = tradeStages.FirstOrDefault(s => s.Id == stageId);
        if (to is null) return new MovePreview(false, "Unknown stage.", []);
        var at = tradeStages.FindIndex(s => s.Id == stageId);
        int RoleIndex(string role) => tradeStages.FindIndex(s => s.Role == role);

        // Gates: dates must be set before the stages that depend on them (keyed by role).
        var delivered = RoleIndex(StageRoles.MaterialsDelivered);
        var labor = RoleIndex(StageRoles.Labor);
        if (delivered >= 0 && at >= delivered && trade.MaterialDate is null)
            return new MovePreview(false, "Set the material delivery date first.", []);
        if (labor >= 0 && at >= labor && trade.WorkDate is null)
            return new MovePreview(false, "Set the work date first.", []);

        var autos = new List<string>();
        var projectStages = await StagesAsync(WorkflowLevels.Project, true, ct);
        var current = projectStages.FirstOrDefault(s => s.Id == project.StageId);
        var currentIdx = current is null ? -1 : projectStages.IndexOf(current);

        var start = projectStages.FirstOrDefault(s => s.Role == StageRoles.ProductionStart);
        var tradeMaterialsOrdered = RoleIndex(StageRoles.MaterialsOrdered);
        if (start is not null && current?.Role == StageRoles.Initial && tradeMaterialsOrdered >= 0 && at >= tradeMaterialsOrdered)
            autos.Add($"Project moves to \"{start.Name}\".");

        var complete = projectStages.FirstOrDefault(s => s.Role == StageRoles.Complete);
        if (complete is not null && to.Role == StageRoles.Complete && currentIdx < projectStages.IndexOf(complete))
        {
            var others = await db.ProjectTrades.Where(t => t.ProjectId == project.Id && t.Id != trade.Id && t.RemovedFromBoardAt == null)
                .Select(t => t.StageId).ToListAsync(ct);
            var completeIds = tradeStages.Where(s => s.Role == StageRoles.Complete).Select(s => s.Id).ToHashSet();
            if (others.All(completeIds.Contains)) autos.Add($"All trades done: project moves to \"{complete.Name}\".");
        }
        return new MovePreview(true, null, autos);
    }

    public async Task<MovePreview> MoveTradeAsync(Guid tradeId, Guid stageId, string? note, CancellationToken ct = default)
    {
        var (trade, project) = await TradeForAsync(tradeId, advance: true, ct);
        if (trade.StageId == stageId) return new MovePreview(true, null, []);
        if (trade.RemovedFromBoardAt is not null) throw new CrmRuleException("Restore this trade to the board before moving it.");
        var plan = await PlanTradeMoveAsync(trade, project, stageId, ct);
        if (!plan.Allowed) throw new CrmRuleException(plan.Blocker!);

        var stages = await StagesAsync(WorkflowLevels.Trade, true, ct);
        var to = stages.First(s => s.Id == stageId);
        var fromName = (await db.WorkflowStages.FirstAsync(s => s.Id == trade.StageId, ct)).Name;
        db.WorkflowStageChanges.Add(new WorkflowStageChange
        {
            EntityType = EntityTypes.Trade, EntityId = trade.Id, FromStageId = trade.StageId, ToStageId = stageId, ByUserId = scope.Me, Note = ContactService.Clean(note),
        });
        trade.StageId = stageId;
        trade.StageChangedAt = clock.UtcNow;
        trade.CompletedAt = to.Role == StageRoles.Complete ? trade.CompletedAt ?? clock.UtcNow : null;
        var tradeName = await db.TradeTypes.Where(t => t.Id == trade.TradeTypeId).Select(t => t.Name).FirstAsync(ct);
        await items.LogAsync(EntityTypes.Trade, trade.Id, "trade.stage_changed", $"{tradeName}: {fromName} → {to.Name}", ct);

        // Automation is forward-only, so re-entering a stage never fires anything twice.
        var projectStages = await StagesAsync(WorkflowLevels.Project, true, ct);
        foreach (var auto in plan.Automations)
        {
            var target = auto.StartsWith("All trades") ? projectStages.First(s => s.Role == StageRoles.Complete) : projectStages.First(s => s.Role == StageRoles.ProductionStart);
            MoveProject(project, projectStages, target, $"Automatic: {tradeName} moved to {to.Name}", automatic: true);
            await items.LogAsync(EntityTypes.Project, project.Id, "project.auto_advanced", $"Moved to {target.Name} automatically ({tradeName} → {to.Name})", ct);
        }
        await db.SaveChangesAsync(ct);
        return plan;
    }

    public async Task MoveProjectAsync(Guid projectId, Guid stageId, string? note, CancellationToken ct = default)
    {
        var project = await (await scope.ProjectsAsync()).FirstOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new RecordNotFoundException("Project");
        if (!await CanAdvanceAsync(project)) throw new RecordForbiddenException("You don't have permission to move work through the workflow.");
        if (project.StageId == stageId) return;
        if (project.RemovedFromBoardAt is not null) throw new CrmRuleException("Restore this project to the board before moving it.");
        var stages = await StagesAsync(WorkflowLevels.Project, true, ct);
        var to = stages.FirstOrDefault(s => s.Id == stageId) ?? throw new CrmRuleException("Unknown stage.");

        // Gate: a project can't be marked complete (or later) while trades are unfinished.
        var completeIdx = stages.FindIndex(s => s.Role == StageRoles.Complete);
        if (completeIdx >= 0 && stages.IndexOf(to) >= completeIdx)
        {
            var completeIds = (await StagesAsync(WorkflowLevels.Trade, false, ct)).Where(s => s.Role == StageRoles.Complete).Select(s => s.Id).ToList();
            var open = await db.ProjectTrades.CountAsync(t => t.ProjectId == projectId && t.RemovedFromBoardAt == null && !completeIds.Contains(t.StageId), ct);
            if (open > 0) throw new CrmRuleException($"{open} trade(s) aren't complete yet.");
        }

        var fromName = (await db.WorkflowStages.FirstAsync(s => s.Id == project.StageId, ct)).Name;
        MoveProject(project, stages, to, note, automatic: false);
        await items.LogAsync(EntityTypes.Project, projectId, "project.stage_changed", $"{fromName} → {to.Name}", ct);
        await db.SaveChangesAsync(ct);
    }

    private void MoveProject(Project project, List<WorkflowStage> stages, WorkflowStage to, string? note, bool automatic)
    {
        db.WorkflowStageChanges.Add(new WorkflowStageChange
        {
            EntityType = EntityTypes.Project, EntityId = project.Id, FromStageId = project.StageId, ToStageId = to.Id,
            ByUserId = scope.Me, IsAutomatic = automatic, Note = ContactService.Clean(note),
        });
        project.StageId = to.Id;
        project.StageChangedAt = clock.UtcNow;
        var completeIdx = stages.FindIndex(s => s.Role == StageRoles.Complete);
        project.CompletedAt = completeIdx >= 0 && stages.IndexOf(to) >= completeIdx ? project.CompletedAt ?? clock.UtcNow : null;
    }

    public async Task UpdateTradeAsync(Guid tradeId, string? description, DateOnly? materialDate, DateOnly? workDate, string? crewNote, CancellationToken ct = default)
    {
        var (trade, _) = await TradeForAsync(tradeId, advance: false, ct);
        var changes = new List<string>();
        if (trade.MaterialDate != materialDate) changes.Add($"material date {materialDate?.ToString("MMM d") ?? "cleared"}");
        if (trade.WorkDate != workDate) changes.Add($"work date {workDate?.ToString("MMM d") ?? "cleared"}");
        trade.Description = ContactService.Clean(description);
        trade.MaterialDate = materialDate;
        trade.WorkDate = workDate;
        trade.CrewNote = ContactService.Clean(crewNote);
        if (changes.Count > 0) await items.LogAsync(EntityTypes.Trade, tradeId, "trade.scheduled", "Scheduled: " + string.Join(", ", changes), ct);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Remove from board ----------------

    public async Task RemoveFromBoardAsync(string entityType, Guid id, string reasonKey, string? note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reasonKey)) throw new CrmRuleException("Pick a reason.");
        if (entityType == EntityTypes.Project)
        {
            var p = await ProjectForEditAsync(id, ct);
            p.RemovedFromBoardAt = clock.UtcNow; p.RemovalReasonKey = reasonKey; p.RemovalNote = ContactService.Clean(note);
            await items.LogAsync(EntityTypes.Project, id, "project.removed_from_board", $"Removed from board ({reasonKey.Replace('_', ' ')}){(note is null ? "" : ": " + note)}", ct);
        }
        else
        {
            var (t, _) = await TradeForAsync(id, advance: false, ct);
            t.RemovedFromBoardAt = clock.UtcNow; t.RemovalReasonKey = reasonKey; t.RemovalNote = ContactService.Clean(note);
            await items.LogAsync(EntityTypes.Trade, id, "trade.removed_from_board", $"Removed from board ({reasonKey.Replace('_', ' ')})", ct);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RestoreToBoardAsync(string entityType, Guid id, CancellationToken ct = default)
    {
        if (entityType == EntityTypes.Project)
        {
            var p = await ProjectForEditAsync(id, ct);
            p.RemovedFromBoardAt = null; p.RemovalReasonKey = null; p.RemovalNote = null;
            await items.LogAsync(EntityTypes.Project, id, "project.restored", "Back on the board", ct);
        }
        else
        {
            var (t, _) = await TradeForAsync(id, advance: false, ct);
            t.RemovedFromBoardAt = null; t.RemovalReasonKey = null; t.RemovalNote = null;
            await items.LogAsync(EntityTypes.Trade, id, "trade.restored", "Back on the board", ct);
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Change orders ----------------

    public async Task<Guid> AddChangeOrderAsync(Guid projectId, Guid? tradeId, string description, decimal amount, string? reason, bool isSupplement, CancellationToken ct = default)
    {
        var p = await ProjectForEditAsync(projectId, ct);
        if (string.IsNullOrWhiteSpace(description)) throw new CrmRuleException("Describe the change.");
        if (amount == 0) throw new CrmRuleException("Enter the change amount (negative for a credit).");
        if (tradeId is { } tid && !await db.ProjectTrades.AnyAsync(t => t.Id == tid && t.ProjectId == projectId, ct)) throw new CrmRuleException("That trade isn't on this project.");
        var seq = (await db.ChangeOrders.Where(c => c.ProjectId == projectId).MaxAsync(c => (int?)c.Sequence, ct) ?? 0) + 1;
        var co = new ChangeOrder
        {
            ProjectId = projectId, ProjectTradeId = tradeId, Sequence = seq, Description = description.Trim(), Amount = amount,
            Reason = ContactService.Clean(reason), IsInsuranceSupplement = isSupplement,
        };
        db.ChangeOrders.Add(co);
        await items.LogAsync(EntityTypes.Project, projectId, "change_order.created", $"Change order #{seq}: {co.Description} ({amount:C2})", ct);
        await db.SaveChangesAsync(ct);
        return co.Id;
    }

    /// <summary>Records the customer's decision. Approved change orders add to the contract value.</summary>
    public async Task DecideChangeOrderAsync(Guid changeOrderId, bool approve, string? approvedByName, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.FinancialsEdit);
        var co = await db.ChangeOrders.FirstOrDefaultAsync(c => c.Id == changeOrderId, ct) ?? throw new RecordNotFoundException("Change order");
        var p = await ProjectForEditAsync(co.ProjectId, ct);
        if (co.Status != ChangeOrderStatuses.Draft) throw new CrmRuleException("This change order has already been decided.");
        if (approve && string.IsNullOrWhiteSpace(approvedByName)) throw new CrmRuleException("Enter who approved it.");
        co.Status = approve ? ChangeOrderStatuses.Approved : ChangeOrderStatuses.Rejected;
        co.ApprovedByName = ContactService.Clean(approvedByName);
        co.DecidedAt = clock.UtcNow;
        co.DecidedByUserId = scope.Me;
        if (approve)
        {
            p.ApprovedChangeOrders += co.Amount;
            if (co.ProjectTradeId is { } tid) (await db.ProjectTrades.FirstAsync(t => t.Id == tid, ct)).Amount += co.Amount;
        }
        await items.LogAsync(EntityTypes.Project, p.Id, approve ? "change_order.approved" : "change_order.rejected",
            $"Change order #{co.Sequence} {(approve ? "approved by " + co.ApprovedByName : "rejected")} ({co.Amount:C2}). Contract now {p.ContractValue:C2}", ct);
        await db.SaveChangesAsync(ct);
    }
}
