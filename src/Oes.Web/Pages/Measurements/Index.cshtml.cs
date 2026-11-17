using Oes.Domain.Crm;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Measurements;

public class IndexModel(MeasurementService measurements) : CrmPageModel
{
    public List<(SetSummary Set, Property Property)> Recent { get; private set; } = [];

    public async Task OnGetAsync() => Recent = await measurements.RecentAsync();
}
