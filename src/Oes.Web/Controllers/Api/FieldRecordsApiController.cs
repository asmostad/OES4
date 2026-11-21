using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Domain.Subcontractors;
using Oes.Infrastructure.Insurance;
using Oes.Infrastructure.Subcontractors;
using Oes.Web.Infrastructure;

namespace Oes.Web.Controllers.Api;

public sealed record WorkOrderListItem(Guid Id, string Number, string Status, string AcceptStatus, string SubName, Guid ProjectId, string ProjectNumber,
    Guid TradeId, string TradeName, string? Address, DateOnly? WorkDate, decimal Amount);
public sealed record WorkOrderLineDto(Guid Id, string Description, decimal Qty, string Unit, decimal Rate, decimal Amount);
public sealed record PersonDto(Guid ContactId, string Name, List<ContactMethodDto> Methods);
public sealed record WorkOrderMobileDto(
    Guid Id, string Number, string Status, string AcceptStatus, string? AcceptedName, DateTime? AcceptedAt, string? DeclineReason, decimal Amount,
    DateOnly? WorkDate, string? ArrivalTime, string? Instructions, DateTime? IssuedAt, Guid? PdfDocumentId, DateTime? CompletedAt, string? CompletionNote,
    PersonDto Sub, Guid ProjectId, string ProjectNumber, string ProjectName, Guid TradeId, string TradeName, string? Address,
    List<WorkOrderLineDto> Lines, bool CanEdit);

public sealed record ClaimListItem(Guid Id, string? ClaimNumber, string Status, string? CarrierName, Guid OpportunityId, string OpportunityNumber,
    string ContactName, string? Address, DateOnly? DateOfLoss, DateOnly? InspectionOn, decimal? ApprovedAmount, int OpenSupplements);
/// <summary>Only what's stored for the carrier (platform defaults plus the company's own edits); nothing is made up.</summary>
public sealed record CarrierDto(string Name, string? Phone, string? ClaimsEmail, string? ClaimsFax, string? Website, string? PreferredSubmission);
public sealed record SupplementDto(Guid Id, string Reason, string Status, decimal? RequestedAmount, decimal? ApprovedAmount, DateOnly? SentOn,
    DateOnly? RespondedOn, string? SubmittedBy, string? Notes);
public sealed record ClaimMobileDto(
    Guid Id, string? ClaimNumber, string? PolicyNumber, string Status, DateOnly? DateOfLoss, string? PerilKey, decimal? Deductible, DateOnly? FiledOn,
    DateOnly? InspectionOn, decimal? ApprovedAmount, decimal? TotalApproved, string? Notes, Guid OpportunityId, string OpportunityNumber,
    string OpportunityName, Guid ContactId, string ContactName, Guid? PropertyId, string? Address, CarrierDto? Carrier, PersonDto? Adjuster,
    PersonDto? DeskAdjuster, string? StormName, List<SupplementDto> Supplements, List<string> Statuses, bool CanEdit, bool IsArchived);
/// <summary>What changes on site: status, claim number, the adjuster's inspection day, the approved amount and notes. The rest stays as it is.</summary>
public sealed record ClaimFieldInput(string Status, string? ClaimNumber, DateOnly? InspectionOn, decimal? ApprovedAmount, string? Notes);

/// <summary>
/// Work orders (on a trade) and insurance claims (on a lead) for the field app. Same visibility and rights as the web:
/// project visibility for work orders, the lead's for claims. Writing and issuing work orders, carriers and
/// supplements stay on the web; on site you can mark a work order done and update a claim after the adjuster meeting.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class FieldRecordsApiController(WorkOrderService workOrders, ClaimService claims, Oes.Application.Abstractions.ICurrentUser user)
    : ControllerBase
{
    private static List<ContactMethodDto> Methods(Contact c) =>
        c.Methods.OrderByDescending(m => m.IsPrimary).Select(m => new ContactMethodDto(m.Kind, m.Value, m.IsPrimary)).ToList();

    [HttpGet("work-orders")]
    public async Task<List<WorkOrderListItem>> WorkOrders(Guid? tradeId, Guid? projectId, CancellationToken ct) =>
        (await workOrders.ListAsync(null, null, tradeId, ct))
            .Where(w => projectId is null || w.ProjectId == projectId)
            .Select(w => new WorkOrderListItem(w.Id, w.Number, w.Status, w.AcceptStatus, w.SubName, w.ProjectId, w.ProjectNumber, w.TradeId, w.TradeName,
                w.Address, w.WorkDate, w.Amount)).ToList();

    [HttpGet("work-orders/{id:guid}")]
    public async Task<WorkOrderMobileDto> WorkOrder(Guid id, CancellationToken ct)
    {
        var d = await workOrders.GetAsync(id, ct);
        var w = d.WorkOrder;
        return new WorkOrderMobileDto(w.Id, w.Number, w.Status, w.AcceptStatus, w.AcceptedName, w.AcceptedAt, w.DeclineReason, w.Amount, w.WorkDate,
            w.ArrivalTime, w.Instructions, w.IssuedAt, w.PdfDocumentId, w.CompletedAt, w.CompletionNote,
            new PersonDto(d.SubContact.Id, d.SubContact.DisplayName, Methods(d.SubContact)), d.Project.Id, d.Project.Number, d.Project.Name, d.Trade.Id,
            d.TradeName, d.Property?.SingleLine,
            d.Lines.Select(l => new WorkOrderLineDto(l.Id, l.Description, l.Qty, l.Unit, l.Rate, l.Amount)).ToList(), d.CanEdit);
    }

    /// <summary>The work is done (from issued). Paying it stays in the office.</summary>
    [HttpPost("work-orders/{id:guid}/complete")]
    [Idempotent]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        await workOrders.SetStatusAsync(id, WorkOrderStatuses.Completed, ct);
        return NoContent();
    }

    [HttpGet("claims")]
    public async Task<List<ClaimListItem>> Claims(Guid? opportunityId, string? q, CancellationToken ct) =>
        (await claims.ListAsync(q, null, null, null, ct))
            .Where(c => opportunityId is null || c.OpportunityId == opportunityId)
            .Select(c => new ClaimListItem(c.Id, c.ClaimNumber, c.Status, c.CarrierName, c.OpportunityId, c.OpportunityNumber, c.ContactName, c.Address,
                c.DateOfLoss, c.InspectionOn, c.ApprovedAmount, c.OpenSupplements)).ToList();

    [HttpGet("claims/{id:guid}")]
    public async Task<ClaimMobileDto> Claim(Guid id, CancellationToken ct)
    {
        var d = await claims.GetAsync(id, ct);
        var c = d.Claim;
        PersonDto? Person(Contact? p) => p is null ? null : new PersonDto(p.Id, p.DisplayName, Methods(p));
        var canEdit = d.CanEdit && c.ArchivedAt is null && await user.HasPermissionAsync(Oes.Application.Security.Permissions.ClaimsManage, ct);
        return new ClaimMobileDto(c.Id, c.ClaimNumber, c.PolicyNumber, c.Status, c.DateOfLoss, c.PerilKey, c.Deductible, c.FiledOn, c.InspectionOn,
            c.ApprovedAmount, d.TotalApproved, c.Notes, d.Opportunity.Id, d.Opportunity.Number, d.Opportunity.Name, d.Contact.Id, d.Contact.DisplayName,
            d.Property?.Id, d.Property?.SingleLine,
            d.Carrier is null ? null : new CarrierDto(d.Carrier.Name, d.Carrier.Phone, d.Carrier.ClaimsEmail, d.Carrier.ClaimsFax, d.Carrier.Website,
                d.Carrier.PreferredSubmission),
            Person(d.Adjuster), Person(d.DeskAdjuster), d.Storm?.Name,
            d.Supplements.Select(s => new SupplementDto(s.Supplement.Id, s.Supplement.Reason, s.Supplement.Status, s.Supplement.RequestedAmount,
                s.Supplement.ApprovedAmount, s.Supplement.SentOn, s.Supplement.RespondedOn, s.SubmittedBy, s.Supplement.Notes)).ToList(),
            [.. Oes.Domain.Insurance.ClaimStatuses.All], canEdit, c.ArchivedAt is not null);
    }

    [HttpPut("claims/{id:guid}/field")]
    public async Task<IActionResult> UpdateClaim(Guid id, ClaimFieldInput input, CancellationToken ct)
    {
        var c = (await claims.GetAsync(id, ct)).Claim;
        await claims.UpdateAsync(id, new ClaimInput
        {
            CarrierChoice = c.TenantCarrierId is { } t ? $"t:{t}" : null, ClaimNumber = input.ClaimNumber, PolicyNumber = c.PolicyNumber,
            DateOfLoss = c.DateOfLoss, PerilKey = c.PerilKey, Deductible = c.Deductible, AdjusterContactId = c.AdjusterContactId,
            DeskAdjusterContactId = c.DeskAdjusterContactId, StormEventId = c.StormEventId, Status = input.Status, FiledOn = c.FiledOn,
            InspectionOn = input.InspectionOn, ApprovedAmount = input.ApprovedAmount, Notes = input.Notes,
        }, ct);
        return NoContent();
    }
}
