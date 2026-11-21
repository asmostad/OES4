using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Insurance;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Claims;

public class IndexModel(ClaimService claims, CarrierService carriers, StormService storms) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Storm { get; set; }
    [BindProperty(SupportsGet = true)] public string? Carrier { get; set; }

    public List<ClaimRow> Rows { get; private set; } = [];
    public List<EffectiveCarrier> Carriers { get; private set; } = [];
    public List<StormRow> Storms { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Rows = await claims.ListAsync(Q, Status, Storm, Carrier);
        Carriers = (await carriers.ListAsync()).Where(c => c.TenantCarrierId is not null).ToList();
        Storms = await storms.ListAsync();
    }
}
