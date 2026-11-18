using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Domain.Subcontractors;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Security;

namespace Oes.Infrastructure.Subcontractors;

public sealed record WorkOrderRow(Guid Id, string Number, string Status, string AcceptStatus, string SubName, Guid SubId, string ProjectNumber,
    Guid ProjectId, Guid TradeId, string TradeName, string? Address, DateOnly? WorkDate, decimal Amount);

public sealed record WorkOrderDetail(
    WorkOrder WorkOrder, List<WorkOrderLine> Lines, Subcontractor Sub, Contact SubContact, string? SubEmail, Domain.Production.Project Project,
    Domain.Production.ProjectTrade Trade, string TradeName, Property? Property, List<SubcontractorRate> Rates, bool LinkActive,
    DateTime? LinkExpiresAt, DateTime? LinkLastUsedAt, bool CanEdit)
{
    public bool IsDraft => WorkOrder.Status == WorkOrderStatuses.Draft;
}

public sealed record IssueResult(string Link, bool Emailed, string? EmailedTo);

/// <summary>
/// Work orders against project trades. Lines are priced from the sub's rates in effect on the
/// work date (copied, like estimate lines). Issuing creates a portal link and emails the sub.
/// </summary>
public sealed class WorkOrderService(
    OesDbContext db, RecordScope scope, RecordItems items, INumberSequence numbers, IClock clock, AccessTokenService tokens,
    ISystemMailer mailer, IConfiguration config, SubcontractorService subs)
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(60);

    public async Task<List<WorkOrderRow>> ListAsync(string? status, Guid? subId, Guid? tradeId, CancellationToken ct = default)
    {
        var projects = await scope.ProjectsAsync(includeArchived: true);
        var wos = db.WorkOrders.Where(w => projects.Any(p => p.Id == w.ProjectId));
        if (!string.IsNullOrWhiteSpace(status)) wos = wos.Where(w => w.Status == status);
        if (subId is { } sid) wos = wos.Where(w => w.SubcontractorId == sid);
        if (tradeId is { } tid) wos = wos.Where(w => w.ProjectTradeId == tid);
        var rows = await (from w in wos
                          join s in db.Subcontractors on w.SubcontractorId equals s.Id
                          join c in db.Contacts on s.ContactId equals c.Id
                          join p in db.Projects on w.ProjectId equals p.Id
                          join t in db.ProjectTrades on w.ProjectTradeId equals t.Id
                          join ty in db.TradeTypes on t.TradeTypeId equals ty.Id
                          join pr in db.Properties on p.PropertyId equals pr.Id into ps from pr in ps.DefaultIfEmpty()
                          orderby w.WorkDate ?? DateOnly.MaxValue, w.Number
                          select new { w, c.DisplayName, SubId = s.Id, p.Number, ProjectId = p.Id, TradeId = t.Id, ty.Name, pr }).Take(500).ToListAsync(ct);
        return rows.Select(r => new WorkOrderRow(r.w.Id, r.w.Number, r.w.Status, r.w.AcceptStatus, r.DisplayName, r.SubId, r.Number, r.ProjectId,
            r.TradeId, r.Name, r.pr?.SingleLine, r.w.WorkDate, r.w.Amount)).ToList();
    }

    public async Task<WorkOrderDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var wo = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new RecordNotFoundException("Work order");
        await scope.EnsureCanSeeAsync(EntityTypes.WorkOrder, id);
        var lines = await db.WorkOrderLines.Where(l => l.WorkOrderId == id).OrderBy(l => l.Sort).ToListAsync(ct);
        var sub = await db.Subcontractors.FirstAsync(s => s.Id == wo.SubcontractorId, ct);
        var contact = await db.Contacts.Include(c => c.Methods).FirstAsync(c => c.Id == sub.ContactId, ct);
        var project = await db.Projects.FirstAsync(p => p.Id == wo.ProjectId, ct);
        var trade = await db.ProjectTrades.FirstAsync(t => t.Id == wo.ProjectTradeId, ct);
        var tradeName = await db.TradeTypes.Where(t => t.Id == trade.TradeTypeId).Select(t => t.Name).FirstAsync(ct);
        var property = project.PropertyId is null ? null : await db.Properties.FirstOrDefaultAsync(p => p.Id == project.PropertyId, ct);
        var rates = await subs.RatesOnAsync(sub.Id, wo.WorkDate ?? DateOnly.FromDateTime(clock.UtcNow), ct);
        var token = await db.ExternalAccessTokens.Where(t => t.Purpose == TokenPurposes.SubPortal && t.EntityType == EntityTypes.WorkOrder && t.EntityId == id && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        var email = contact.Methods.Where(m => m.Kind == ContactMethodKinds.Email).OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault();
        return new WorkOrderDetail(wo, lines, sub, contact, email, project, trade, tradeName, property,
            rates.Where(r => r.TradeTypeId is null || r.TradeTypeId == trade.TradeTypeId).ToList(),
            token is not null && token.ExpiresAt > clock.UtcNow, token?.ExpiresAt, token?.LastUsedAt, await CanManageAsync());
    }

    private async Task<bool> CanManageAsync()
    {
        try { await scope.EnsurePermissionAsync(Permissions.WorkOrdersManage); return true; }
        catch (RecordForbiddenException) { return false; }
    }

    private async Task<WorkOrder> EditableAsync(Guid id, bool draftOnly, CancellationToken ct)
    {
        await scope.EnsurePermissionAsync(Permissions.WorkOrdersManage);
        var wo = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new RecordNotFoundException("Work order");
        await scope.EnsureCanSeeAsync(EntityTypes.WorkOrder, id);
        if (draftOnly && wo.Status != WorkOrderStatuses.Draft) throw new CrmRuleException("Lines can only change while the work order is a draft. Use Revise to reopen it.");
        return wo;
    }

    public async Task<Guid> CreateAsync(Guid tradeId, Guid subcontractorId, DateOnly? workDate, string? arrivalTime, string? instructions, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.WorkOrdersManage);
        await scope.EnsureCanSeeAsync(EntityTypes.Trade, tradeId);
        var trade = await db.ProjectTrades.FirstAsync(t => t.Id == tradeId, ct);
        var sub = await db.Subcontractors.FirstOrDefaultAsync(s => s.Id == subcontractorId, ct) ?? throw new RecordNotFoundException("Subcontractor");
        if (sub.Status != SubStatuses.Active) throw new CrmRuleException("That subcontractor isn't active.");
        var wo = new WorkOrder
        {
            Number = await numbers.NextAsync(NumberedKinds.WorkOrder, ct), ProjectId = trade.ProjectId, ProjectTradeId = tradeId,
            SubcontractorId = subcontractorId, WorkDate = workDate ?? trade.WorkDate, ArrivalTime = ContactService.Clean(arrivalTime),
            Instructions = ContactService.Clean(instructions),
        };
        db.WorkOrders.Add(wo);
        var subName = await db.Contacts.Where(c => c.Id == sub.ContactId).Select(c => c.DisplayName).FirstAsync(ct);
        await items.LogAsync(EntityTypes.Trade, tradeId, "work_order.created", $"Work order {wo.Number} drafted for {subName}", ct);
        await db.SaveChangesAsync(ct);
        return wo.Id;
    }

    public async Task UpdateAsync(Guid id, DateOnly? workDate, string? arrivalTime, string? instructions, CancellationToken ct = default)
    {
        var wo = await EditableAsync(id, draftOnly: false, ct);
        if (wo.Status is WorkOrderStatuses.Paid or WorkOrderStatuses.Cancelled) throw new CrmRuleException("This work order is closed.");
        wo.WorkDate = workDate;
        wo.ArrivalTime = ContactService.Clean(arrivalTime);
        wo.Instructions = ContactService.Clean(instructions);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddLineAsync(Guid id, Guid? rateId, string? description, decimal qty, string? unit, decimal? rate, CancellationToken ct = default)
    {
        var wo = await EditableAsync(id, draftOnly: true, ct);
        if (qty <= 0) throw new CrmRuleException("Quantity must be more than zero.");
        var line = new WorkOrderLine { WorkOrderId = id, Qty = qty, Sort = (await db.WorkOrderLines.Where(l => l.WorkOrderId == id).MaxAsync(l => (int?)l.Sort, ct) ?? 0) + 10 };
        if (rateId is { } rid)
        {
            var r = await db.SubcontractorRates.FirstOrDefaultAsync(x => x.Id == rid && x.SubcontractorId == wo.SubcontractorId, ct)
                    ?? throw new CrmRuleException("That rate isn't this subcontractor's.");
            line.RateId = r.Id; line.Description = r.Description; line.Unit = r.Unit; line.Rate = r.Rate;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(description) || rate is null or < 0) throw new CrmRuleException("Describe the work and enter a rate.");
            line.Description = description.Trim(); line.Unit = string.IsNullOrWhiteSpace(unit) ? "EA" : unit.Trim().ToUpperInvariant(); line.Rate = rate.Value;
        }
        db.WorkOrderLines.Add(line);
        await db.SaveChangesAsync(ct);
        await RecalcAsync(wo, ct);
    }

    public async Task RemoveLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await db.WorkOrderLines.FirstOrDefaultAsync(l => l.Id == lineId, ct) ?? throw new RecordNotFoundException("Line");
        var wo = await EditableAsync(line.WorkOrderId, draftOnly: true, ct);
        db.WorkOrderLines.Remove(line);
        await db.SaveChangesAsync(ct);
        await RecalcAsync(wo, ct);
    }

    private async Task RecalcAsync(WorkOrder wo, CancellationToken ct)
    {
        wo.Amount = (await db.WorkOrderLines.Where(l => l.WorkOrderId == wo.Id).ToListAsync(ct)).Sum(l => l.Amount);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Issues the work order: creates a fresh portal link and emails it to the sub when they have an email.</summary>
    public async Task<IssueResult> IssueAsync(Guid id, CancellationToken ct = default)
    {
        var wo = await EditableAsync(id, draftOnly: true, ct);
        if (!await db.WorkOrderLines.AnyAsync(l => l.WorkOrderId == id, ct)) throw new CrmRuleException("Add at least one line before issuing.");
        if (wo.WorkDate is null) throw new CrmRuleException("Set the work date before issuing.");
        wo.Status = WorkOrderStatuses.Issued;
        wo.IssuedAt = clock.UtcNow;
        wo.AcceptStatus = AcceptStatuses.Pending;
        wo.AcceptedName = null; wo.AcceptedAt = null; wo.DeclineReason = null;
        await db.SaveChangesAsync(ct);
        var result = await SendLinkAsync(wo, ct);
        await items.LogAsync(EntityTypes.WorkOrder, id, "work_order.issued", $"Issued {wo.Amount:C2}{(result.Emailed ? " and emailed to " + result.EmailedTo : " (no email on file: share the link)")}", ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Revokes the old link and creates a new one (lost or forwarded link).</summary>
    public async Task<IssueResult> NewLinkAsync(Guid id, CancellationToken ct = default)
    {
        var wo = await EditableAsync(id, draftOnly: false, ct);
        if (wo.Status != WorkOrderStatuses.Issued) throw new CrmRuleException("Only issued work orders have a portal link.");
        var result = await SendLinkAsync(wo, ct);
        await items.LogAsync(EntityTypes.WorkOrder, id, "work_order.link_rotated", "New portal link created; the old one stopped working", ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<IssueResult> SendLinkAsync(WorkOrder wo, CancellationToken ct)
    {
        var raw = await tokens.IssueAsync(TokenPurposes.SubPortal, EntityTypes.WorkOrder, wo.Id, LinkLifetime, ct);
        var baseUrl = config["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl is not configured.");
        var link = $"{baseUrl.TrimEnd('/')}/portal/{raw}";
        var sub = await db.Subcontractors.FirstAsync(s => s.Id == wo.SubcontractorId, ct);
        var email = await db.ContactMethods.Where(m => m.ContactId == sub.ContactId && m.Kind == ContactMethodKinds.Email)
            .OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefaultAsync(ct);
        if (email is null) return new IssueResult(link, false, null);
        var company = await db.Tenants.Where(t => t.Id == wo.TenantId).Select(t => t.Name).FirstAsync(ct);
        var enc = HtmlEncoder.Default;
        await mailer.SendAsync(new SystemEmail(email, $"Work order {wo.Number} from {company}",
            $"<p>{enc.Encode(company)} sent you work order <strong>{enc.Encode(wo.Number)}</strong> for {wo.WorkDate:dddd, MMMM d}.</p>" +
            $"<p><a href=\"{enc.Encode(link)}\">View, accept or decline the work order</a></p>" +
            "<p>This link is just for you. It expires in 60 days and stops working if a new link is sent.</p>", link), ct);
        return new IssueResult(link, true, email);
    }

    /// <summary>Back to draft for changes: the portal link stops working until it's issued again.</summary>
    public async Task ReviseAsync(Guid id, CancellationToken ct = default)
    {
        var wo = await EditableAsync(id, draftOnly: false, ct);
        if (wo.Status != WorkOrderStatuses.Issued) throw new CrmRuleException("Only an issued work order can be revised.");
        wo.Status = WorkOrderStatuses.Draft;
        await tokens.RevokeAllAsync(TokenPurposes.SubPortal, EntityTypes.WorkOrder, id, ct);
        await items.LogAsync(EntityTypes.WorkOrder, id, "work_order.revised", "Reopened for changes; portal link revoked", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        var wo = await EditableAsync(id, draftOnly: false, ct);
        var allowed = (wo.Status, status) switch
        {
            (WorkOrderStatuses.Issued, WorkOrderStatuses.Completed) => true,
            (WorkOrderStatuses.Completed, WorkOrderStatuses.Paid) => true,
            (WorkOrderStatuses.Draft or WorkOrderStatuses.Issued, WorkOrderStatuses.Cancelled) => true,
            _ => false,
        };
        if (!allowed) throw new CrmRuleException($"A {wo.Status} work order can't become {status}.");
        wo.Status = status;
        var now = clock.UtcNow;
        if (status == WorkOrderStatuses.Completed) wo.CompletedAt ??= now;
        if (status == WorkOrderStatuses.Paid) wo.PaidAt = now;
        if (status == WorkOrderStatuses.Cancelled) { wo.CancelledAt = now; await tokens.RevokeAllAsync(TokenPurposes.SubPortal, EntityTypes.WorkOrder, id, ct); }
        await items.LogAsync(EntityTypes.WorkOrder, id, "work_order." + status, $"Marked {status}", ct);
        await db.SaveChangesAsync(ct);
    }
}
