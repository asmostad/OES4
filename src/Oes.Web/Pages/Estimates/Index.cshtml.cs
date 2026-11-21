using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Estimating;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

public class IndexModel(EstimateService estimates) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public bool Mine { get; set; }
    public List<EstimateRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => Rows = await estimates.ListAsync(Q, Status, Mine);
}
