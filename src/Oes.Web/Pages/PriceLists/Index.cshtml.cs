using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Estimating;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.PriceLists;

[HasPermission(Permissions.PriceListsManage)]
public class IndexModel(CatalogService catalog) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public bool ShowArchived { get; set; }
    public List<PriceListRow> Lists { get; private set; } = [];

    public async Task OnGetAsync() => Lists = await catalog.PriceListsAsync(ShowArchived);

    public Task<IActionResult> OnPostCreateAsync(PriceListInput input) => Guard(async () =>
    {
        var id = await catalog.SaveListAsync(null, input);
        Flash("Price list created. Import a CSV or copy prices into it.");
        return RedirectToPage("/PriceLists/View", new { id });
    });

    public Task<IActionResult> OnPostCopyAsync(Guid sourceId, string name, DateOnly effectiveDate) => Guard(async () =>
    {
        var id = await catalog.CopyListAsync(sourceId, name, effectiveDate);
        Flash("Copied. The new list is a draft: update prices, then set it to active.");
        return RedirectToPage("/PriceLists/View", new { id });
    });
}
