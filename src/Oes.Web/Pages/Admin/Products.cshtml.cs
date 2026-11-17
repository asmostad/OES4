using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Estimating;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.ProductsManage)]
public class ProductsModel(CatalogService catalog, MeasurementService measurements) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowInactive { get; set; }
    public List<Product> Products { get; private set; } = [];
    public List<TradeType> Trades { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Products = await catalog.ProductsAsync(Q, ShowInactive);
        Trades = await measurements.TradesAsync();
    }

    public Task<IActionResult> OnPostSaveAsync(Guid? id, ProductInput input) => Guard(async () =>
    {
        await catalog.SaveProductAsync(id, input);
        Flash("Product saved.");
        return RedirectToPage(new { q = Q, showInactive = ShowInactive });
    });
}
