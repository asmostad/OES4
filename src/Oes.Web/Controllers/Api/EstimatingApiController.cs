using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;

namespace Oes.Web.Controllers.Api;

/// <summary>Typeahead sources for the estimate builder. Price items come only from the estimate's own price list.</summary>
[ApiController]
[Route("api/v1")]
public sealed class EstimatingApiController(EstimateService estimates, CatalogService catalog) : ControllerBase
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

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
