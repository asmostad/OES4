using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Estimating;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.ProductsManage)]
public class TakeoffRuleModel(TakeoffService takeoff, MeasurementService measurements, CatalogService catalog) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Trade { get; set; }
    [BindProperty] public TakeoffRuleInput Input { get; set; } = new();

    public TradeType TradeType { get; private set; } = null!;
    public List<TakeoffDriver> Drivers { get; private set; } = [];
    public List<Product> Products { get; private set; } = [];
    public List<string> Slots { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Id is { } id)
        {
            var r = await takeoff.GetRuleAsync(id);
            Input = new TakeoffRuleInput
            {
                Id = r.Id, TradeTypeId = r.TradeTypeId, Name = r.Name, ProductId = r.ProductId, Slot = r.Slot, Driver = r.Driver, Multiplier = r.Multiplier,
                WastePercent = r.WastePercent, UseSuggestedWaste = r.UseSuggestedWaste, RoundMode = r.RoundMode, RoundTo = r.RoundTo, MinQty = r.MinQty, MaxQty = r.MaxQty,
                Cond1Field = r.Cond1Field, Cond1Op = r.Cond1Op, Cond1Value = r.Cond1Value, Cond2Field = r.Cond2Field, Cond2Op = r.Cond2Op, Cond2Value = r.Cond2Value,
                OptionKey = r.OptionKey, OptionLabel = r.OptionLabel, OptionDefaultOn = r.OptionDefaultOn, Description = r.Description, ScopeText = r.ScopeText, Notes = r.Notes, IsActive = r.IsActive,
            };
        }
        else
        {
            var trade = (await measurements.TradesAsync()).FirstOrDefault(t => t.Code == Trade);
            if (trade is null) return NotFound();
            Input.TradeTypeId = trade.Id;
        }
        await LoadAsync();
        return Page();
    });

    public Task<IActionResult> OnPostAsync() => Guard(async () =>
    {
        Input.Id = Id;
        if (Id is { } id) Input.TradeTypeId = (await takeoff.GetRuleAsync(id)).TradeTypeId;
        await LoadAsync();
        await takeoff.SaveRuleAsync(Input);
        Flash("Rule saved.");
        return RedirectToPage("/Admin/Takeoff", new { trade = TradeType.Code });
    }, Page);

    private async Task LoadAsync()
    {
        TradeType = (await measurements.TradesAsync()).First(t => t.Id == Input.TradeTypeId);
        Drivers = await takeoff.DriversAsync(TradeType.Id);
        Products = (await catalog.ProductsAsync(null, false)).Where(p => p.TradeTypeId == null || p.TradeTypeId == TradeType.Id).ToList();
        Slots = await takeoff.SlotsAsync();
    }
}
