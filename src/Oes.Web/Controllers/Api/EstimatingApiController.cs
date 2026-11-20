using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Web.Infrastructure;

namespace Oes.Web.Controllers.Api;

public sealed record EstimateListItem(Guid Id, string Number, string Name, string Status, decimal Total, Guid OpportunityId, string ContactName,
    string? Address, string? OwnerName, int LatestVersionNo, int PendingApprovals, DateTime UpdatedAt);
public sealed record EstimateLineDto(Guid Id, string Kind, string? Code, string Description, decimal Qty, string Unit, decimal UnitPrice, decimal Amount,
    string ApprovalStatus, string? Notes);
public sealed record EstimateSectionDto(Guid Id, string Name, string? TradeName, string? TradeColor, string? ScopeOfWork, decimal Subtotal, List<EstimateLineDto> Lines);
public sealed record EstimateVersionDto(Guid Id, int VersionNo, string Status, decimal Total, DateTime IssuedAt, string? IssuedBy, string? RevisionReason,
    string? SignerName, DateTime? SignedAt, bool HasPdf);
public sealed record EstimateMobileDto(
    Guid Id, string Number, string Name, string Status, string Method, Guid OpportunityId, string OpportunityName, Guid ContactId, string ContactName,
    string? Address, string? OwnerName, string? ClaimNumber, string? PriceListName, DateOnly? ExpiresOn,
    decimal Subtotal, decimal OverheadPct, decimal Overhead, decimal ProfitPct, decimal Profit, decimal TaxRate, decimal Tax, decimal Total,
    int PendingApprovals, int RejectedLines, bool CanEdit, bool IsArchived, List<EstimateSectionDto> Sections, List<EstimateVersionDto> Versions);
public sealed record IssueInput(string? RevisionReason);
public sealed record SignInput(string? SignerName);
public sealed record DeclineInput(string? Reason);
public sealed record IssuedDto(Guid VersionId);

/// <summary>
/// Estimates for the field app (reading, issuing, recording a signature or a decline), plus the typeahead
/// sources for the estimate builder. Price items come only from the estimate's own price list. Building an
/// estimate stays on the web. The proposal PDF is fetched from the proposal page's API route (handler=Copy).
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class EstimatingApiController(EstimateService estimates, CatalogService catalog) : ControllerBase
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    [HttpGet("estimates")]
    public async Task<List<EstimateListItem>> List(Guid? opportunityId, string? q, bool mine = false, CancellationToken ct = default) =>
        (await estimates.ListAsync(q, null, mine, opportunityId, ct))
            .Select(r => new EstimateListItem(r.Id, r.Number, r.Name, r.Status, r.Total, r.OpportunityId, r.ContactName, r.Address, r.OwnerName,
                r.LatestVersionNo, r.PendingApprovals, r.UpdatedAt)).ToList();

    [HttpGet("estimates/{id:guid}")]
    public async Task<EstimateMobileDto> Get(Guid id, CancellationToken ct)
    {
        var d = await estimates.GetAsync(id, ct);
        var e = d.Estimate;
        return new EstimateMobileDto(e.Id, e.Number, e.Name, e.Status, e.Method, d.Opportunity.Id, d.Opportunity.Name, d.Contact.Id, d.Contact.DisplayName,
            d.Property?.SingleLine, d.OwnerName, d.Claim?.ClaimNumber, d.PriceList?.Name, e.ExpiresOn,
            e.Subtotal, e.OverheadPct, e.Overhead, e.ProfitPct, e.Profit, e.TaxRate, e.Tax, e.Total,
            d.PendingApprovals, d.RejectedLines, d.CanEdit, e.ArchivedAt is not null,
            d.Sections.Select(s => new EstimateSectionDto(s.Section.Id, s.Section.Name, s.TradeName, s.TradeColor, s.Section.ScopeOfWork, s.Subtotal,
                s.Lines.Select(l => new EstimateLineDto(l.Id, l.Kind, l.Code, l.Description, l.Qty, l.Unit, l.UnitPrice, l.Amount, l.ApprovalStatus, l.Notes))
                    .ToList())).ToList(),
            d.Versions.Select(v => new EstimateVersionDto(v.Id, v.VersionNo, v.Status, v.Total, v.IssuedAt, v.IssuedBy, v.RevisionReason, v.SignerName,
                v.SignedAt, (v.Status == VersionStatuses.Signed ? v.SignedPdfDocumentId : v.PdfDocumentId) is not null)).ToList());
    }

    /// <summary>Freezes the estimate as a new version (same rules as the web: no waiting or rejected price exceptions).</summary>
    [HttpPost("estimates/{id:guid}/issue")]
    [Idempotent]
    public async Task<IssuedDto> Issue(Guid id, IssueInput input, CancellationToken ct) => new(await estimates.IssueAsync(id, input.RevisionReason, ct));

    /// <summary>Records who signed the latest issued version; the opportunity moves to won.</summary>
    [HttpPost("estimate-versions/{versionId:guid}/sign")]
    [Idempotent]
    public async Task<IActionResult> Sign(Guid versionId, SignInput input, CancellationToken ct)
    {
        await estimates.MarkSignedAsync(versionId, input.SignerName ?? "", ct);
        return NoContent();
    }

    [HttpPost("estimate-versions/{versionId:guid}/decline")]
    [Idempotent]
    public async Task<IActionResult> Decline(Guid versionId, DeclineInput input, CancellationToken ct)
    {
        await estimates.MarkDeclinedAsync(versionId, input.Reason, ct);
        return NoContent();
    }

    [HttpGet("estimates/{id:guid}/price-items")]
    public async Task<ActionResult<List<LookupItem>>> PriceItems(Guid id, string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return new List<LookupItem>();
        EstimateDetail detail;
        try { detail = await estimates.GetAsync(id, ct); }
        catch (RecordNotFoundException) { return NotFound(); }
        if (detail.Estimate.PriceListId is not { } listId) return new List<LookupItem>();
        var rows = await catalog.SearchItemsAsync(listId, q.Trim(), 25, ct);
        return rows.Select(r => new LookupItem(r.PriceListItemId, $"{r.CategoryCode} {r.Code} · {r.Description}",
            $"{Activities.Label(r.Activity)} · {r.UnitPrice.ToString("C2", Us)} / {r.Unit}".TrimStart(' ', '·'))).ToList();
    }

    [HttpGet("lookup/products")]
    public async Task<List<LookupItem>> Products(string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return [];
        var rows = await catalog.ProductsAsync(q.Trim(), false, ct);
        return rows.Take(25).Select(p => new LookupItem(p.Id, $"{p.Sku} · {p.Name}", $"{p.DefaultPrice.ToString("C2", Us)} / {p.Unit}")).ToList();
    }
}
