using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages;

public class IndexModel(OpportunityService opportunities) : CrmPageModel
{
    public OpportunityService.DashboardCounts Counts { get; private set; } = new(0, 0, 0, 0, 0);
    public List<OpportunityRow> Recent { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Counts = await opportunities.DashboardAsync();
        Recent = (await opportunities.SearchAsync(null, null, Oes.Domain.Crm.Outcomes.Open, mine: true, page: 1, pageSize: 8)).Items;
    }
}
