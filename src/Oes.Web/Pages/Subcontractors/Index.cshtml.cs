using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Subcontractors;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Subcontractors;

public class IndexModel(SubcontractorService subs, MeasurementService measurements) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Trade { get; set; }
    [BindProperty(SupportsGet = true)] public bool All { get; set; }

    public List<SubRow> Rows { get; private set; } = [];
    public List<TradeType> Trades { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Rows = await subs.ListAsync(string.IsNullOrWhiteSpace(Q) ? null : Q.Trim(), Trade, !All);
        Trades = await measurements.TradesAsync();
        return Page();
    });
}
