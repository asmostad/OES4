using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Admin;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.SettingsManage)]
public class SettingsModel(SettingsService settings) : CrmPageModel
{
    [BindProperty] public CompanySettings Input { get; set; } = new();

    public async Task OnGetAsync() => Input = await settings.GetAsync();

    public Task<IActionResult> OnPostAsync() => Guard(async () =>
    {
        await settings.SaveAsync(Input);
        Flash("Settings saved.");
        return RedirectToPage();
    }, Page);
}
