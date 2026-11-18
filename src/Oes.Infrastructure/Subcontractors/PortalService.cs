using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Domain.Subcontractors;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Files;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Security;

namespace Oes.Infrastructure.Subcontractors;

public sealed record PortalView(
    Guid WorkOrderId, string Number, string Status, string AcceptStatus, string? AcceptedName, DateTime? AcceptedAt, string CompanyName,
    string SubName, string TradeName, string? Address, string? PropertyNotes, DateOnly? WorkDate, string? ArrivalTime, string? Instructions,
    List<WorkOrderLine> Lines, decimal Amount, DateTime? CompletedAt, string? CompletionNote, int PhotosUploaded);

/// <summary>
/// What a sub can do through their link, with no account: see the work order, accept or decline,
/// and report completion with photos. The token is the authorization; every call re-checks it.
/// </summary>
public sealed class PortalService(OesDbContext db, AccessTokenService tokens, RecordItems items, IClock clock, Work.NotificationService notifications)
{
    /// <summary>Tells the project manager what the sub did.</summary>
    private async Task NotifyOwnerAsync(WorkOrder wo, string kind, string title, CancellationToken ct)
    {
        var owner = await db.Projects.Where(p => p.Id == wo.ProjectId).Select(p => p.OwnerUserId).FirstOrDefaultAsync(ct);
        notifications.Notify(owner, kind, title, null, $"/WorkOrders/View/{wo.Id}");
    }

    public const int MaxPhotos = 20;

    private async Task<WorkOrder?> ResolveAsync(string token, CancellationToken ct)
    {
        var t = await tokens.ResolveAsync(token, TokenPurposes.SubPortal, ct);
        if (t is null || t.EntityType != EntityTypes.WorkOrder) return null;
        var wo = await db.WorkOrders.FirstOrDefaultAsync(w => w.Id == t.EntityId, ct);
        return wo is null || wo.Status is WorkOrderStatuses.Draft or WorkOrderStatuses.Cancelled ? null : wo;
    }

    public async Task<PortalView?> GetAsync(string token, CancellationToken ct = default)
    {
        var wo = await ResolveAsync(token, ct);
        if (wo is null) return null;
        var company = await db.Tenants.Where(t => t.Id == wo.TenantId).Select(t => t.Name).FirstAsync(ct);
        var sub = await (from s in db.Subcontractors where s.Id == wo.SubcontractorId join c in db.Contacts on s.ContactId equals c.Id select c.DisplayName).FirstAsync(ct);
        var trade = await (from t in db.ProjectTrades where t.Id == wo.ProjectTradeId join ty in db.TradeTypes on t.TradeTypeId equals ty.Id select ty.Name).FirstAsync(ct);
        var project = await db.Projects.FirstAsync(p => p.Id == wo.ProjectId, ct);
        var property = project.PropertyId is null ? null : await db.Properties.FirstOrDefaultAsync(p => p.Id == project.PropertyId, ct);
        var lines = await db.WorkOrderLines.Where(l => l.WorkOrderId == wo.Id).OrderBy(l => l.Sort).ToListAsync(ct);
        var photos = await db.Documents.CountAsync(d => d.EntityType == EntityTypes.WorkOrder && d.EntityId == wo.Id && d.Source == "portal", ct);
        return new PortalView(wo.Id, wo.Number, wo.Status, wo.AcceptStatus, wo.AcceptedName, wo.AcceptedAt, company, sub, trade,
            property?.SingleLine, property?.Notes, wo.WorkDate, wo.ArrivalTime, wo.Instructions, lines, wo.Amount, wo.CompletedAt, wo.CompletionNote, photos);
    }

    public async Task<bool> RespondAsync(string token, bool accept, string name, string? reason, CancellationToken ct = default)
    {
        var wo = await ResolveAsync(token, ct);
        if (wo is null || wo.Status != WorkOrderStatuses.Issued) return false;
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150) throw new CrmRuleException("Type your name.");
        if (!accept && string.IsNullOrWhiteSpace(reason)) throw new CrmRuleException("Tell us why you can't take it.");
        wo.AcceptStatus = accept ? AcceptStatuses.Accepted : AcceptStatuses.Declined;
        wo.AcceptedName = name.Trim();
        wo.AcceptedAt = clock.UtcNow;
        wo.DeclineReason = accept ? null : reason!.Trim()[..Math.Min(reason.Trim().Length, 500)];
        await items.LogAsync(EntityTypes.WorkOrder, wo.Id, accept ? "work_order.accepted" : "work_order.declined",
            accept ? $"Accepted by {wo.AcceptedName} (sub portal)" : $"Declined by {wo.AcceptedName}: {wo.DeclineReason}", ct);
        await NotifyOwnerAsync(wo, accept ? "work_order.accepted" : "work_order.declined",
            accept ? $"{wo.Number} accepted by {wo.AcceptedName}" : $"{wo.Number} declined: {wo.DeclineReason}", ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CompleteAsync(string token, string? note, IReadOnlyList<(Stream Content, string FileName, string ContentType)> photos, CancellationToken ct = default)
    {
        var wo = await ResolveAsync(token, ct);
        if (wo is null || wo.Status != WorkOrderStatuses.Issued) return false;
        if (wo.AcceptStatus != AcceptStatuses.Accepted) throw new CrmRuleException("Accept the work order first.");
        if (photos.Count > MaxPhotos) throw new CrmRuleException($"Upload up to {MaxPhotos} photos at a time.");
        foreach (var (content, fileName, contentType) in photos)
        {
            if (!contentType.StartsWith("image/") && contentType != "application/pdf") throw new CrmRuleException("Photos must be images (or PDF).");
            try { await items.UploadFromLinkAsync(EntityTypes.WorkOrder, wo.Id, content, fileName, contentType, "photos", ct); }
            catch (FileRejectedException ex) { throw new CrmRuleException(ex.Message); }
        }
        wo.CompletionNote = ContactService.Clean(note)?[..Math.Min(note!.Trim().Length, 2000)];
        wo.Status = WorkOrderStatuses.Completed;
        wo.CompletedAt = clock.UtcNow;
        await items.LogAsync(EntityTypes.WorkOrder, wo.Id, "work_order.completed", $"Reported complete by the sub{(photos.Count > 0 ? $" with {photos.Count} photo(s)" : "")}", ct);
        await NotifyOwnerAsync(wo, "work_order.completed", $"{wo.Number} reported complete{(photos.Count > 0 ? $" ({photos.Count} photos)" : "")}", ct);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
