using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Insurance;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.CarriersManage)]
public class CarriersModel(CarrierService carriers) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public bool ShowHidden { get; set; }
    public List<EffectiveCarrier> Carriers { get; private set; } = [];

    public async Task OnGetAsync() => Carriers = await carriers.ListAsync(includeHidden: ShowHidden);

    public Task<IActionResult> OnPostHideAsync(string choice, bool hidden) => Guard(async () =>
    {
        await carriers.SetHiddenAsync(choice, hidden);
        Flash(hidden ? "Carrier hidden from pick lists." : "Carrier shown again.");
        return RedirectToPage(new { showHidden = ShowHidden });
    });
}
