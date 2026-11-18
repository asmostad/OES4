using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.BlastsManage)]
public class SuppressionsModel(ConsentService consent) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string Channel { get; set; } = Channels.Email;
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public List<SuppressionRow> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!Channels.All.Contains(Channel)) return NotFound();
        Rows = await consent.SuppressionsAsync(Channel, Q);
        return Page();
    }

    public Task<IActionResult> OnPostAddAsync(string address, string scope) => Guard(async () =>
    {
        await consent.AddManualSuppressionAsync(Channel, address, scope);
        Flash("Blocked.");
        return RedirectToPage(new { channel = Channel });
    });

    public Task<IActionResult> OnPostRemoveAsync(Guid id) => Guard(async () =>
    {
        await consent.RemoveSuppressionAsync(id);
        Flash("Unblocked.");
        return RedirectToPage(new { channel = Channel });
    });
}
