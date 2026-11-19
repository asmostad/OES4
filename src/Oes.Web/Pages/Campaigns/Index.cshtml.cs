using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Campaigns;

[HasPermission(Permissions.LeadsCampaigns)]
public class IndexModel(CampaignService campaigns) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public bool Inactive { get; set; }
    public List<CampaignRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => Rows = await campaigns.ListAsync(Inactive);
}
