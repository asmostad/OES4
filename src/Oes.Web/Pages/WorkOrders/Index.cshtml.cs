using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Subcontractors;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.WorkOrders;

public class IndexModel(WorkOrderService workOrders) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    public List<WorkOrderRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => Rows = await workOrders.ListAsync(Status, null, null);
}
