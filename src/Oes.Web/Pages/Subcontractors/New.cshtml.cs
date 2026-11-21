using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Subcontractors;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Subcontractors;

[HasPermission(Permissions.SubcontractorsManage)]
public class NewModel(SubcontractorService subs, MeasurementService measurements) : CrmPageModel
{
    [BindProperty] public NewSubInput Input { get; set; } = new();
    [BindProperty] public string? ContactLabel { get; set; }
    public List<TradeType> Trades { get; private set; } = [];

    public async Task OnGetAsync() => Trades = await measurements.TradesAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        Trades = await measurements.TradesAsync();
        return await Guard(async () =>
        {
            var id = await subs.CreateAsync(Input);
            Flash("Subcontractor added. Upload their insurance and add rates next.");
            return RedirectToPage("/Subcontractors/View", new { id });
        }, Page);
    }
}
