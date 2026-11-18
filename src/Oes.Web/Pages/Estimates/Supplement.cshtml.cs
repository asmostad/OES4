using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Persistence;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

/// <summary>Carrier scope vs our estimate, item by item: a printable supplement request.</summary>
public class SupplementModel(ScopeImportService imports, EstimateService estimates, OesDbContext db, ITenantContext tenant) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool All { get; set; }

    public SupplementCompare Compare { get; private set; } = null!;
    public EstimateDetail Detail { get; private set; } = null!;
    public Tenant Company { get; private set; } = null!;

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Compare = await imports.CompareAsync(Id);
        Detail = await estimates.GetAsync(Compare.Estimate.Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        return Page();
    });
}
