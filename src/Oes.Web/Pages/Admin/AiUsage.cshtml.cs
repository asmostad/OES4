using Oes.Application.Security;
using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.AiManage)]
public class AiUsageModel(AssistantService assistant, TenantTime time, Microsoft.Extensions.Options.IOptions<AiOptions> options) : CrmPageModel
{
    public List<UsageRow> Rows { get; private set; } = [];
    public AiOptions Settings => options.Value;
    public bool Connected => assistant.IsConfigured;

    public async Task OnGetAsync()
    {
        var today = await time.TodayAsync();
        Rows = await assistant.UsageAsync(today.AddDays(-29), today);
    }
}
