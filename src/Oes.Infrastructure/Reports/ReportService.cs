using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Domain.Production;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Reports;

public sealed record ReportFilter(DateOnly From, DateOnly To, Guid? OwnerId = null);

/// <summary>A table a report page renders (with an optional bar per row) and exports as CSV.</summary>
public sealed class ReportTable(string title, params string[] columns)
{
    public string Title { get; } = title;
    public string[] Columns { get; } = columns;
    public List<object?[]> Rows { get; } = [];
    /// <summary>Per-row colour for the bar (stage colours); null = default.</summary>
    public List<string?> Colors { get; } = [];
    /// <summary>Per-row link (to the record); null = none.</summary>
    public List<string?> Links { get; } = [];
    /// <summary>Index of the numeric column drawn as a bar, or -1.</summary>
    public int BarColumn { get; init; } = -1;
    /// <summary>Columns holding money (formatted as currency).</summary>
    public int[] MoneyColumns { get; init; } = [];
    /// <summary>Columns holding a 0–1 ratio (formatted as a percentage).</summary>
    public int[] PercentColumns { get; init; } = [];
    public object?[]? Totals { get; set; }

    public void Add(object?[] row, string? color = null, string? link = null)
    {
        Rows.Add(row);
        Colors.Add(color);
        Links.Add(link);
    }
}

public sealed record Kpi(string Label, object Value, string Kind = "number");

public sealed record Report(string Title, List<Kpi> Kpis, List<ReportTable> Tables);

public sealed record ReportKind(string Key, string Title, string Icon, string Blurb, bool UsesRange);

/// <summary>
/// Reports over what the user may see: the same record scope as every list (own and unassigned
/// records, or everything with records.view_all), so a report never reveals hidden records.
/// Date ranges are whole days in the company's time zone.
/// </summary>
public sealed class ReportService(OesDbContext db, RecordScope scope, ICurrentUser user, TenantTime time, CrmLookups lookups, IClock clock)
{
    public static readonly IReadOnlyList<ReportKind> Kinds =
    [
        new("pipeline", "Pipeline", "bi-funnel", "Open opportunities by stage and owner, right now.", false),
        new("sales", "Sales", "bi-trophy", "New leads, wins, losses and win rate by month, source and owner.", true),
        new("production", "Production", "bi-kanban", "Active projects and trades by stage, backlog, sold and completed.", true),
        new("stale", "Stale", "bi-hourglass-split", "Opportunities and projects stuck in a stage too long.", false),
        new("tasks", "Tasks", "bi-check2-square", "Open, overdue and finished tasks by person.", true),
        new("calls", "Prospecting calls", "bi-telephone-outbound", "Call outcomes by outcome and by person.", true),
        new("marketing", "Marketing", "bi-megaphone", "Email blast results and the leads they brought in, and nurture workflows.", true),
    ];

    public Task<Report> RunAsync(string kind, ReportFilter f, CancellationToken ct = default) => kind switch
    {
        "pipeline" => PipelineAsync(f, ct),
        "sales" => SalesAsync(f, ct),
        "production" => ProductionAsync(f, ct),
        "stale" => StaleAsync(f, ct),
        "calls" => CallsAsync(f, ct),
        "marketing" => MarketingAsync(f, ct),
        "tasks" => TasksAsync(f, ct),
        _ => throw new CrmRuleException("Unknown report."),
    };

    private async Task<IQueryable<Opportunity>> OppsAsync(ReportFilter f)
    {
        await scope.EnsurePermissionAsync(Permissions.ReportsView);
        var q = await scope.OpportunitiesAsync(includeArchived: true);
        if (f.OwnerId is { } o) q = q.Where(x => x.OwnerUserId == o);
        return q;
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(CancellationToken ct) =>
        await (from m in db.Memberships join u in db.Users on m.UserId equals u.Id select new { u.Id, Name = u.FirstName + " " + u.LastName })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

    private static string Owner(Dictionary<Guid, string> names, Guid? id) => id is { } g ? names.GetValueOrDefault(g, "Former user") : "Unassigned";

    // ---------------- Pipeline (open, right now) ----------------

    public async Task<Report> PipelineAsync(ReportFilter f, CancellationToken ct = default)
    {
        var open = (await OppsAsync(f)).Where(o => o.Outcome == Outcomes.Open && o.ArchivedAt == null);
        var rows = await open.Select(o => new { o.StageId, o.OwnerUserId, Value = o.ExpectedValue ?? 0 }).ToListAsync(ct);
        var stages = await db.PipelineStages.Where(s => s.Outcome == Outcomes.Open).OrderBy(s => s.Sort).ToListAsync(ct);
        var names = await UserNamesAsync(ct);

        var byStage = new ReportTable("Open opportunities by stage", "Stage", "Opportunities", "Expected value") { BarColumn = 1, MoneyColumns = [2] };
        foreach (var s in stages)
        {
            var inStage = rows.Where(r => r.StageId == s.Id).ToList();
            byStage.Add([s.Name, inStage.Count, inStage.Sum(r => r.Value)], s.Color, $"/Opportunities?view=list&stage={s.Id}");
        }
        byStage.Totals = ["Total", rows.Count, rows.Sum(r => r.Value)];

        var byOwner = new ReportTable("Open opportunities by owner", "Owner", "Opportunities", "Expected value") { BarColumn = 2, MoneyColumns = [2] };
        foreach (var g in rows.GroupBy(r => r.OwnerUserId).OrderByDescending(g => g.Sum(r => r.Value)))
            byOwner.Add([Owner(names, g.Key), g.Count(), g.Sum(r => r.Value)]);

        return new Report("Pipeline", [
            new Kpi("Open opportunities", rows.Count),
            new Kpi("Expected value", rows.Sum(r => r.Value), "money"),
            new Kpi("Average value", rows.Count == 0 ? 0m : rows.Sum(r => r.Value) / rows.Count, "money"),
        ], [byStage, byOwner]);
    }

    // ---------------- Sales: leads, wins, losses, sources ----------------

    public async Task<Report> SalesAsync(ReportFilter f, CancellationToken ct = default)
    {
        var (start, end) = await time.DayRangeUtcAsync(f.From, f.To);
        var opps = await OppsAsync(f);
        var created = await opps.Where(o => o.CreatedAt >= start && o.CreatedAt < end).Select(o => new { o.SourceKey, o.OwnerUserId, o.CreatedAt }).ToListAsync(ct);
        var won = await opps.Where(o => o.Outcome == Outcomes.Won && o.WonAt >= start && o.WonAt < end)
            .Select(o => new { o.Id, o.SourceKey, o.OwnerUserId, At = o.WonAt!.Value, o.ExpectedValue }).ToListAsync(ct);
        var lost = await opps.Where(o => o.Outcome == Outcomes.Lost && o.ClosedAt >= start && o.ClosedAt < end)
            .Select(o => new { o.SourceKey, o.OwnerUserId, o.LostReasonKey }).ToListAsync(ct);

        // Won value: the signed contract where a project exists, otherwise the expected value.
        var wonIds = won.Select(w => w.Id).ToList();
        var contracts = await db.Projects.Where(p => wonIds.Contains(p.OpportunityId)).GroupBy(p => p.OpportunityId)
            .Select(g => new { g.Key, Total = g.Sum(p => p.OriginalContract) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        decimal Value(Guid id, decimal? expected) => contracts.TryGetValue(id, out var c) ? c : expected ?? 0;
        var wonValue = won.Sum(w => Value(w.Id, w.ExpectedValue));
        double? Rate(int w, int l) => w + l == 0 ? null : (double)w / (w + l);

        var sources = (await lookups.ValuesAsync(LookupTypes.LeadSource, ct)).ToDictionary(o => o.Value, o => o.Label);
        string Source(string? key) => key is null ? "Not set" : sources.GetValueOrDefault(key, key);
        var bySource = new ReportTable("By lead source", "Source", "New leads", "Won", "Lost", "Win rate", "Won value") { BarColumn = 1, MoneyColumns = [5], PercentColumns = [4] };
        foreach (var key in created.Select(c => c.SourceKey).Concat(won.Select(w => w.SourceKey)).Concat(lost.Select(l => l.SourceKey)).Distinct()
                     .OrderByDescending(k => created.Count(c => c.SourceKey == k)))
        {
            var w = won.Where(x => x.SourceKey == key).ToList();
            var l = lost.Count(x => x.SourceKey == key);
            bySource.Add([Source(key), created.Count(c => c.SourceKey == key), w.Count, l, Rate(w.Count, l), w.Sum(x => Value(x.Id, x.ExpectedValue))]);
        }

        var names = await UserNamesAsync(ct);
        var byOwner = new ReportTable("By owner", "Owner", "New leads", "Won", "Lost", "Win rate", "Won value") { BarColumn = 5, MoneyColumns = [5], PercentColumns = [4] };
        foreach (var key in created.Select(c => c.OwnerUserId).Concat(won.Select(w => w.OwnerUserId)).Concat(lost.Select(l => l.OwnerUserId)).Distinct())
        {
            var w = won.Where(x => x.OwnerUserId == key).ToList();
            var l = lost.Count(x => x.OwnerUserId == key);
            byOwner.Add([Owner(names, key), created.Count(c => c.OwnerUserId == key), w.Count, l, Rate(w.Count, l), w.Sum(x => Value(x.Id, x.ExpectedValue))]);
        }
        byOwner.Rows.Sort((a, b) => ((decimal)b[5]!).CompareTo((decimal)a[5]!));

        var zone = await time.ZoneAsync();
        string Month(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("yyyy-MM");
        var byMonth = new ReportTable("By month", "Month", "New leads", "Won", "Won value") { BarColumn = 1, MoneyColumns = [3] };
        for (var m = new DateOnly(f.From.Year, f.From.Month, 1); m <= f.To; m = m.AddMonths(1))
        {
            var key = m.ToString("yyyy-MM");
            var w = won.Where(x => Month(x.At) == key).ToList();
            byMonth.Add([m.ToString("MMM yyyy"), created.Count(c => Month(c.CreatedAt) == key), w.Count, w.Sum(x => Value(x.Id, x.ExpectedValue))]);
        }

        var reasons = (await lookups.ValuesAsync(LookupTypes.LostReason, ct)).ToDictionary(o => o.Value, o => o.Label);
        var lostWhy = new ReportTable("Why we lost", "Reason", "Lost") { BarColumn = 1 };
        foreach (var g in lost.GroupBy(l => l.LostReasonKey).OrderByDescending(g => g.Count()))
            lostWhy.Add([g.Key is null ? "Not given" : reasons.GetValueOrDefault(g.Key, g.Key), g.Count()]);

        return new Report("Sales", [
            new Kpi("New leads", created.Count),
            new Kpi("Won", won.Count),
            new Kpi("Win rate", (object?)Rate(won.Count, lost.Count) ?? "–", Rate(won.Count, lost.Count) is null ? "text" : "percent"),
            new Kpi("Won value", wonValue, "money"),
        ], [byMonth, bySource, byOwner, lostWhy]);
    }

    // ---------------- Production ----------------

    public async Task<Report> ProductionAsync(ReportFilter f, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ReportsView);
        var (start, end) = await time.DayRangeUtcAsync(f.From, f.To);
        var projects = await scope.ProjectsAsync(includeArchived: true);
        if (f.OwnerId is { } o) projects = projects.Where(p => p.OwnerUserId == o);
        var active = await projects.Where(p => p.CompletedAt == null && p.RemovedFromBoardAt == null && p.ArchivedAt == null)
            .Select(p => new { p.Id, p.StageId, Value = p.OriginalContract + p.ApprovedChangeOrders }).ToListAsync(ct);
        var completed = await projects.Where(p => p.CompletedAt >= start && p.CompletedAt < end)
            .Select(p => new { Value = p.OriginalContract + p.ApprovedChangeOrders }).ToListAsync(ct);
        var sold = await projects.Where(p => p.CreatedAt >= start && p.CreatedAt < end).Select(p => p.OriginalContract).ToListAsync(ct);

        var stages = await db.WorkflowStages.OrderBy(s => s.Sort).ToListAsync(ct);
        var byStage = new ReportTable("Active projects by stage", "Stage", "Projects", "Contract value") { BarColumn = 1, MoneyColumns = [2] };
        foreach (var s in stages.Where(s => s.Level == WorkflowLevels.Project))
        {
            var inStage = active.Where(p => p.StageId == s.Id).ToList();
            if (inStage.Count > 0 || s.IsActive) byStage.Add([s.Name, inStage.Count, inStage.Sum(p => p.Value)], s.Color);
        }

        var activeIds = active.Select(p => p.Id).ToList();
        var trades = await (from t in db.ProjectTrades where activeIds.Contains(t.ProjectId) && t.RemovedFromBoardAt == null && t.CompletedAt == null
                            join ty in db.TradeTypes on t.TradeTypeId equals ty.Id
                            select new { t.StageId, Trade = ty.Name, t.Amount }).ToListAsync(ct);
        var tradesByStage = new ReportTable("Open trades by stage", "Stage", "Trades", "Amount") { BarColumn = 1, MoneyColumns = [2] };
        foreach (var s in stages.Where(s => s.Level == WorkflowLevels.Trade))
        {
            var inStage = trades.Where(t => t.StageId == s.Id).ToList();
            if (inStage.Count > 0 || s.IsActive) tradesByStage.Add([s.Name, inStage.Count, inStage.Sum(t => t.Amount)], s.Color);
        }
        var byTrade = new ReportTable("Open trades by type", "Trade", "Trades", "Amount") { BarColumn = 2, MoneyColumns = [2] };
        foreach (var g in trades.GroupBy(t => t.Trade).OrderByDescending(g => g.Sum(t => t.Amount))) byTrade.Add([g.Key, g.Count(), g.Sum(t => t.Amount)]);

        return new Report("Production", [
            new Kpi("Active projects", active.Count),
            new Kpi("Backlog value", active.Sum(p => p.Value), "money"),
            new Kpi("Sold in range", sold.Sum(), "money"),
            new Kpi("Completed in range", completed.Count),
        ], [byStage, tradesByStage, byTrade]);
    }

    // ---------------- Stale: stuck in a stage too long ----------------

    public async Task<Report> StaleAsync(ReportFilter f, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var names = await UserNamesAsync(ct);
        var opps = await (from o in await OppsAsync(f)
                          where o.Outcome == Outcomes.Open && o.ArchivedAt == null
                          join s in db.PipelineStages on o.StageId equals s.Id
                          where s.StaleAfterHours != null
                          join c in db.Contacts on o.PrimaryContactId equals c.Id
                          select new { o.Id, o.Number, Contact = c.DisplayName, Stage = s.Name, s.Color, Hours = s.StaleAfterHours!.Value, Since = o.StageChangedAt ?? o.CreatedAt, o.OwnerUserId, o.ExpectedValue })
            .ToListAsync(ct);
        var staleOpps = new ReportTable("Stale opportunities", "Opportunity", "Contact", "Stage", "Days in stage", "Owner", "Expected value") { BarColumn = 3, MoneyColumns = [5] };
        foreach (var x in opps.Where(x => (now - x.Since).TotalHours > x.Hours).OrderByDescending(x => now - x.Since))
            staleOpps.Add([x.Number, x.Contact, x.Stage, (int)(now - x.Since).TotalDays, Owner(names, x.OwnerUserId), x.ExpectedValue ?? 0], x.Color, $"/Opportunities/View/{x.Id}");

        var projects = await scope.ProjectsAsync();
        if (f.OwnerId is { } owner) projects = projects.Where(p => p.OwnerUserId == owner);
        var proj = await (from p in projects
                          where p.CompletedAt == null && p.RemovedFromBoardAt == null
                          join s in db.WorkflowStages on p.StageId equals s.Id
                          where s.StaleAfterDays != null
                          select new { p.Id, p.Number, p.Name, Stage = s.Name, s.Color, Days = s.StaleAfterDays!.Value, Since = p.StageChangedAt ?? p.CreatedAt, p.OwnerUserId })
            .ToListAsync(ct);
        var staleProjects = new ReportTable("Stale projects", "Project", "Name", "Stage", "Days in stage", "Project manager") { BarColumn = 3 };
        foreach (var x in proj.Where(x => (now - x.Since).TotalDays > x.Days).OrderByDescending(x => now - x.Since))
            staleProjects.Add([x.Number, x.Name, x.Stage, (int)(now - x.Since).TotalDays, Owner(names, x.OwnerUserId)], x.Color, $"/Projects/View/{x.Id}");

        return new Report("Stale", [
            new Kpi("Stale opportunities", staleOpps.Rows.Count),
            new Kpi("Stale projects", staleProjects.Rows.Count),
        ], [staleOpps, staleProjects]);
    }

    // ---------------- Tasks ----------------

    public async Task<Report> TasksAsync(ReportFilter f, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ReportsView);
        var (start, end) = await time.DayRangeUtcAsync(f.From, f.To);
        var tasks = db.Tasks.AsQueryable();
        if (!await user.HasPermissionAsync(Permissions.TasksViewAll, ct)) tasks = tasks.Where(t => t.AssignedToUserId == scope.Me);
        if (f.OwnerId is { } o) tasks = tasks.Where(t => t.AssignedToUserId == o);
        var today = await time.TodayAsync();
        var (todayStart, _) = await time.DayRangeUtcAsync(today, today);
        var now = clock.UtcNow;
        var rows = await tasks.Where(t => t.CompletedAt == null || (t.CompletedAt >= start && t.CompletedAt < end))
            .Select(t => new { t.AssignedToUserId, t.CompletedAt, t.DueAt, t.HasDueTime }).ToListAsync(ct);
        bool Overdue(DateTime? due, bool hasTime) => due is { } d && (hasTime ? d < now : d < todayStart);
        var names = await UserNamesAsync(ct);
        var byUser = new ReportTable("Tasks by person", "Person", "Open", "Overdue", "Done in range") { BarColumn = 1 };
        foreach (var g in rows.GroupBy(r => r.AssignedToUserId).OrderByDescending(g => g.Count(r => r.CompletedAt == null)))
            byUser.Add([Owner(names, g.Key), g.Count(r => r.CompletedAt == null), g.Count(r => r.CompletedAt == null && Overdue(r.DueAt, r.HasDueTime)), g.Count(r => r.CompletedAt != null)]);
        return new Report("Tasks", [
            new Kpi("Open", rows.Count(r => r.CompletedAt == null)),
            new Kpi("Overdue", rows.Count(r => r.CompletedAt == null && Overdue(r.DueAt, r.HasDueTime))),
            new Kpi("Done in range", rows.Count(r => r.CompletedAt != null)),
        ], [byUser]);
    }

    // ---------------- Marketing (blasts and nurture workflows) ----------------

    /// <summary>
    /// Email blasts started in the range (sent, failed, skipped, unsubscribes and new leads from
    /// recipients within 30 days) and nurture workflows (enrollments started in the range and how
    /// they ended). Lead and win counts only include opportunities the user can see.
    /// </summary>
    public async Task<Report> MarketingAsync(ReportFilter f, CancellationToken ct = default)
    {
        var opps = await OppsAsync(f);
        var (start, end) = await time.DayRangeUtcAsync(f.From, f.To);

        var blasts = await db.Blasts.Where(b => b.StartedAt >= start && b.StartedAt < end).OrderBy(b => b.StartedAt)
            .Select(b => new { b.Id, b.Name, StartedAt = b.StartedAt!.Value }).ToListAsync(ct);
        var blastIds = blasts.Select(b => b.Id).ToList();
        var recipients = await db.BlastRecipients.Where(r => blastIds.Contains(r.BlastId))
            .Select(r => new { r.BlastId, r.ContactId, r.Address, r.Status }).ToListAsync(ct);
        var addresses = recipients.Select(r => r.Address).Distinct().ToList();
        var unsubscribes = await db.Suppressions.Where(s => s.Channel == Domain.Communication.Channels.Email
                && s.Reason == Domain.Communication.SuppressionReasons.Unsubscribe && addresses.Contains(s.Address))
            .Select(s => new { s.Address, s.CreatedAt }).ToListAsync(ct);
        var contactIds = recipients.Select(r => r.ContactId).Distinct().ToList();
        var leads = await opps.Where(o => contactIds.Contains(o.PrimaryContactId) && o.CreatedAt >= start)
            .Select(o => new { o.PrimaryContactId, o.CreatedAt, o.Outcome }).ToListAsync(ct);

        var blastTable = new ReportTable("Email blasts", "Blast", "Sent on", "Sent", "Failed", "Skipped", "Unsubscribed", "New leads (30 days)", "Won") { BarColumn = 2 };
        int sentTotal = 0, unsubTotal = 0, leadTotal = 0, wonTotal = 0;
        foreach (var b in blasts)
        {
            var rs = recipients.Where(r => r.BlastId == b.Id).ToList();
            var sent = rs.Where(r => r.Status == Domain.Communication.BlastRecipientStatuses.Sent).ToList();
            var window = b.StartedAt.AddDays(30);
            var sentAddresses = sent.Select(r => r.Address).ToHashSet();
            var sentContacts = sent.Select(r => r.ContactId).ToHashSet();
            var unsub = unsubscribes.Count(u => sentAddresses.Contains(u.Address) && u.CreatedAt >= b.StartedAt && u.CreatedAt < window);
            var newLeads = leads.Where(l => sentContacts.Contains(l.PrimaryContactId) && l.CreatedAt >= b.StartedAt && l.CreatedAt < window).ToList();
            var won = newLeads.Count(l => l.Outcome == Outcomes.Won);
            blastTable.Add([b.Name, DateOnly.FromDateTime(await time.ToLocalAsync(b.StartedAt)).ToString("MMM d, yyyy"), sent.Count,
                rs.Count(r => r.Status == Domain.Communication.BlastRecipientStatuses.Failed), rs.Count(r => r.Status == Domain.Communication.BlastRecipientStatuses.Skipped),
                unsub, newLeads.Count, won], link: $"/Blasts/View?id={b.Id}");
            sentTotal += sent.Count; unsubTotal += unsub; leadTotal += newLeads.Count; wonTotal += won;
        }
        if (blasts.Count > 0) blastTable.Totals = ["Total", "", sentTotal, null, null, unsubTotal, leadTotal, wonTotal];

        var sequences = await db.Sequences.Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var enrollments = await db.Enrollments.Where(e => e.CreatedAt >= start && e.CreatedAt < end)
            .Select(e => new { e.SequenceId, e.Status, e.StopReason, e.OpportunityId }).ToListAsync(ct);
        var enrolledOpps = enrollments.Where(e => e.OpportunityId != null).Select(e => e.OpportunityId!.Value).Distinct().ToList();
        var wonOpps = (await opps.Where(o => enrolledOpps.Contains(o.Id) && o.Outcome == Outcomes.Won).Select(o => o.Id).ToListAsync(ct)).ToHashSet();
        var nurture = new ReportTable("Nurture workflows (enrolled in range)", "Workflow", "Enrolled", "Still running", "Finished", "Stopped", "Opportunities won") { BarColumn = 1 };
        foreach (var g in enrollments.GroupBy(e => e.SequenceId).OrderByDescending(g => g.Count()))
            nurture.Add([sequences.GetValueOrDefault(g.Key, "Deleted workflow"), g.Count(),
                g.Count(e => e.Status == Domain.Communication.EnrollmentStatuses.Active), g.Count(e => e.Status == Domain.Communication.EnrollmentStatuses.Completed),
                g.Count(e => e.Status == Domain.Communication.EnrollmentStatuses.Stopped),
                g.Where(e => e.OpportunityId is { } id && wonOpps.Contains(id)).Select(e => e.OpportunityId).Distinct().Count()],
                link: $"/Admin/NurtureEdit?id={g.Key}");

        var stops = new ReportTable("Why nurture runs stopped", "Reason", "Runs") { BarColumn = 1 };
        foreach (var g in enrollments.Where(e => e.Status == Domain.Communication.EnrollmentStatuses.Stopped)
                     .GroupBy(e => string.IsNullOrWhiteSpace(e.StopReason) ? "Not recorded" : Fmt(e.StopReason!)).OrderByDescending(g => g.Count()))
            stops.Add([g.Key, g.Count()]);

        return new Report("Marketing", [
            new Kpi("Blast emails sent", sentTotal),
            new Kpi("Unsubscribed after a blast", unsubTotal),
            new Kpi("New leads from blasts", leadTotal),
            new Kpi("Nurture enrollments", enrollments.Count),
        ], [blastTable, nurture, stops]);

        static string Fmt(string s) => char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' ');
    }

    // ---------------- Prospecting calls ----------------

    /// <summary>Call outcomes recorded in the range, by outcome and by person, over leads the user can see.</summary>
    public async Task<Report> CallsAsync(ReportFilter f, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ReportsView);
        var opps = await scope.OpportunitiesAsync(includeArchived: true);
        var (start, end) = await time.DayRangeUtcAsync(f.From, f.To);
        var calls = db.OpportunityDispositions.Where(d => d.CreatedAt >= start && d.CreatedAt < end && opps.Any(o => o.Id == d.OpportunityId));
        if (f.OwnerId is { } owner) calls = calls.Where(d => d.ByUserId == owner);
        var rows = await (from c in calls
                          join d in db.Dispositions on c.DispositionId equals d.Id
                          select new { c.ByUserId, c.OpportunityId, d.Name, d.Color, d.Sort, d.CountsAsAttempt, d.IsDoNotContact, d.MovesToStageId }).ToListAsync(ct);
        var openStages = (await db.PipelineStages.Where(s => s.Outcome == Outcomes.Open).Select(s => s.Id).ToListAsync(ct)).ToHashSet();
        bool Forward(Guid? stage) => stage is { } s && openStages.Contains(s);

        var byOutcome = new ReportTable("Calls by outcome", "Outcome", "Calls", "Share") { BarColumn = 1, PercentColumns = [2] };
        foreach (var g in rows.GroupBy(r => new { r.Name, r.Color, r.Sort }).OrderBy(g => g.Key.Sort))
            byOutcome.Add([g.Key.Name, g.Count(), rows.Count == 0 ? 0m : (decimal)g.Count() / rows.Count], g.Key.Color);

        var names = await UserNamesAsync(ct);
        var byPerson = new ReportTable("Calls by person", "Person", "Calls", "Leads called", "Moved forward", "Do not contact") { BarColumn = 1 };
        foreach (var g in rows.GroupBy(r => r.ByUserId).OrderByDescending(g => g.Count()))
            byPerson.Add([Owner(names, g.Key), g.Count(), g.Select(r => r.OpportunityId).Distinct().Count(), g.Count(r => Forward(r.MovesToStageId)), g.Count(r => r.IsDoNotContact)]);

        return new Report("Prospecting calls", [
            new Kpi("Calls logged", rows.Count),
            new Kpi("Leads called", rows.Select(r => r.OpportunityId).Distinct().Count()),
            new Kpi("Moved forward", rows.Count(r => Forward(r.MovesToStageId))),
            new Kpi("Do not contact", rows.Count(r => r.IsDoNotContact)),
        ], [byOutcome, byPerson]);
    }
}
