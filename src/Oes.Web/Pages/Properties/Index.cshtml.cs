using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Properties;

public class IndexModel(PropertyService properties) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public PagedResult<PropertyRow> Result { get; private set; } = new([], 0, 1, 25);

    public async Task OnGetAsync() => Result = await properties.SearchAsync(Q, Kind, PageNumber);
}
