using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Insurance;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Claims;

public class ViewModel(ClaimService claims, RecordItems items, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public ClaimDetail Detail { get; private set; } = null!;
    public TimelineModel Timeline { get; private set; } = null!;
    public string? PerilLabel { get; private set; }

    public Task<IActionResult> OnGetAsync(string? tab) => Guard(async () =>
    {
        Detail = await claims.GetAsync(Id);
        Timeline = await TimelineModel.LoadAsync(items, lookups, EntityTypes.Claim, Id, tab, null);
        PerilLabel = await lookups.LabelAsync(LookupTypes.Peril, Detail.Claim.PerilKey);
        return Page();
    });

    public Task<IActionResult> OnPostSupplementAsync(Guid? supplementId, SupplementInput supplement) => Guard(async () =>
    {
        await claims.SaveSupplementAsync(Id, supplementId, supplement);
        Flash(supplementId is null ? "Supplement added." : "Supplement updated.");
        return RedirectToPage(new { id = Id });
    });
}
