using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.ProductsManage)]
public class TradesModel(MeasurementService measurements) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Trade { get; set; }

    public List<TradeType> Trades { get; private set; } = [];
    public TradeType? Selected { get; private set; }
    public List<MeasurementFieldDef> Fields { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Trades = await measurements.TradesAsync(activeOnly: false);
        Selected = Trades.FirstOrDefault(t => t.Id == Trade) ?? Trades.FirstOrDefault(t => t.MeasurementMode != MeasurementModes.None);
        if (Selected is not null) Fields = await measurements.FieldsAsync(Selected.Id, activeOnly: false);
    }

    public Task<IActionResult> OnPostTradeAsync(Guid? id, string name, string mode, string color, bool isActive) => Guard(async () =>
    {
        var saved = await measurements.SaveTradeAsync(id, name, mode, color, isActive);
        Flash("Trade saved.");
        return RedirectToPage(new { trade = saved });
    });

    public Task<IActionResult> OnPostFieldAsync(Guid tradeId, Guid? id, string label, string unit, string? group, bool isActive) => Guard(async () =>
    {
        await measurements.SaveFieldAsync(tradeId, id, label, unit, group, isActive);
        Flash("Field saved.");
        return RedirectToPage(new { trade = tradeId });
    });
}
