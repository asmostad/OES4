using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages;

public class IndexModel(OpportunityService opportunities, WorkService work) : CrmPageModel
{
    public OpportunityService.DashboardCounts Counts { get; private set; } = new(0, 0, 0, 0, 0);
    public List<OpportunityRow> Recent { get; private set; } = [];
    public WorkService.MyDay Day { get; private set; } = new(0, 0, []);

    public async Task OnGetAsync()
    {
        Counts = await opportunities.DashboardAsync();
        Recent = (await opportunities.SearchAsync(null, null, Oes.Domain.Crm.Outcomes.Open, mine: true, page: 1, pageSize: 8)).Items;
        Day = await work.MyDayAsync();
    }
}
