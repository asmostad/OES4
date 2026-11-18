using Oes.Application.Security;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Blasts;

[HasPermission(Permissions.BlastsManage)]
public class IndexModel(BlastService blasts, Oes.Infrastructure.Work.TenantTime time) : CrmPageModel
{
    public List<BlastRow> Rows { get; private set; } = [];
    public Dictionary<Guid, DateTime> Local { get; } = [];

    public async Task OnGetAsync()
    {
        Rows = await blasts.ListAsync();
        foreach (var r in Rows) Local[r.Id] = await time.ToLocalAsync(r.ScheduledAt ?? r.CreatedAt);
    }
}
