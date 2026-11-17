using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Estimating;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Persistence;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

/// <summary>
/// The proposal, rendered from a frozen version (never live data), behind sign-in. Print or
/// save as PDF from the browser; server-side PDF rendering (Playwright) comes later.
/// </summary>
public class ProposalModel(EstimateService estimates, OesDbContext db, ITenantContext tenant) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public EstimateVersion Version { get; private set; } = null!;
    public VersionContent Doc { get; private set; } = null!;
    public Estimate Estimate { get; private set; } = null!;
    public Tenant Company { get; private set; } = null!;

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        (Version, Doc, Estimate) = await estimates.GetVersionAsync(Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        return Page();
    });
}
