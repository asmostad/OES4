using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.ProductsManage)]
public class TakeoffModel(TakeoffService takeoff, MeasurementService measurements, CatalogService catalog) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Trade { get; set; }

    public List<TradeType> Trades { get; private set; } = [];
    public TradeType? Current { get; private set; }
    public List<TakeoffRuleRow> Rules { get; private set; } = [];
    public Dictionary<string, string> DriverLabels { get; private set; } = [];
    /// <summary>Slot → how many active products fill it.</summary>
    public Dictionary<string, int> SlotProducts { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Trades = (await measurements.TradesAsync()).Where(t => t.MeasurementMode != MeasurementModes.None).ToList();
        Current = Trades.FirstOrDefault(t => t.Code == Trade) ?? Trades.FirstOrDefault();
        if (Current is null) return;
        Rules = await takeoff.RulesAsync(Current.Id);
        DriverLabels = (await takeoff.DriversAsync(Current.Id)).ToDictionary(d => d.Key, d => d.Label + (d.Unit.Length > 0 ? $" ({d.Unit})" : ""));
        var products = await catalog.ProductsAsync(null, false);
        SlotProducts = Rules.Where(r => r.Rule.Slot != null).Select(r => r.Rule.Slot!).Distinct()
            .ToDictionary(s => s, s => products.Count(p => p.TakeoffSlot == s && (p.TradeTypeId == null || p.TradeTypeId == Current.Id)));
    }

    public Task<IActionResult> OnPostMoveAsync(Guid id, int direction) => Guard(async () =>
    {
        await takeoff.MoveRuleAsync(id, direction);
        return RedirectToPage(new { trade = Trade });
    });
}
