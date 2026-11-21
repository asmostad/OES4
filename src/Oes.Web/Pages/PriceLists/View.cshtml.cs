using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.PriceLists;

[HasPermission(Permissions.PriceListsManage)]
[RequestSizeLimit(30 * 1024 * 1024)]
public class ViewModel(CatalogService catalog) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PriceList List { get; private set; } = null!;
    public PagedResult<PriceItemRow> Items { get; private set; } = new([], 0, 1, 50);
    public ImportResult? Imported { get; private set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () => { await LoadAsync(); return Page(); });

    public Task<IActionResult> OnPostSettingsAsync(PriceListInput input) => Guard(async () =>
    {
        await catalog.SaveListAsync(Id, input);
        Flash("Price list saved.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostPriceAsync(Guid itemId, decimal unitPrice) => Guard(async () =>
    {
        await catalog.SetPriceAsync(itemId, unitPrice);
        Flash("Price updated. Existing estimates keep the price they copied.");
        return RedirectToPage(new { id = Id, q = Q, p = PageNumber });
    });

    public async Task<IActionResult> OnPostImportAsync(IFormFile? file)
    {
        return await Guard(async () =>
        {
            if (file is null || file.Length == 0) throw new CrmRuleException("Choose a CSV file.");
            if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new CrmRuleException("Upload a .csv file (save Excel sheets as CSV first).");
            using var reader = new StreamReader(file.OpenReadStream());
            Imported = await catalog.ImportCsvAsync(Id, reader);
            await LoadAsync();
            return Page();
        }, () => Page());
    }

    private async Task LoadAsync()
    {
        List = await catalog.GetListAsync(Id);
        Items = await catalog.ItemsAsync(Id, Q, PageNumber);
    }
}
