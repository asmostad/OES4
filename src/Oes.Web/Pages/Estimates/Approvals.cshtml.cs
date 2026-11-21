using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Estimating;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

[HasPermission(Permissions.PricingApprove)]
public class ApprovalsModel(EstimateService estimates) : CrmPageModel
{
    public List<ApprovalRow> Rows { get; private set; } = [];
    public Guid? Me { get; private set; }

    public async Task OnGetAsync([FromServices] Oes.Application.Abstractions.ICurrentUser user)
    {
        Rows = await estimates.PendingApprovalsAsync();
        Me = user.UserId;
    }

    public Task<IActionResult> OnPostDecideAsync(Guid lineId, bool approve, string? note) => Guard(async () =>
    {
        await estimates.DecideAsync(lineId, approve, note);
        Flash(approve ? "Approved." : "Rejected.");
        return RedirectToPage();
    });
}
