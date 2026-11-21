using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Insurance;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Insurance;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Storms;

public class ViewModel(StormService storms, OpportunityService opportunities, ClaimService claims) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public StormEvent Storm { get; private set; } = null!;
    public StormRow Stats { get; private set; } = null!;
    public List<OpportunityRow> Opportunities { get; private set; } = [];
    public List<ClaimRow> Claims { get; private set; } = [];
    public List<PropertyRow> Untouched { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Storm = await storms.GetAsync(Id);
        Stats = (await storms.ListAsync(includeArchived: true)).First(s => s.Id == Id);
        Opportunities = (await opportunities.SearchAsync(null, null, null, false, 1, 200, default, Id)).Items;
        Claims = await claims.ListAsync(null, null, Id, null);
        Untouched = await storms.UntouchedPropertiesAsync(Id);
        return Page();
    });
}
