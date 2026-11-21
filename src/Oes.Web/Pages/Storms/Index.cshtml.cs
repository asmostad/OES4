using Oes.Infrastructure.Insurance;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Storms;

public class IndexModel(StormService storms) : CrmPageModel
{
    public List<StormRow> Storms { get; private set; } = [];

    public async Task OnGetAsync() => Storms = await storms.ListAsync();
}
