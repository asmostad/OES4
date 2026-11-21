using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Admin;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.SettingsManage)]
public class SettingsModel(SettingsService settings) : CrmPageModel
{
    [BindProperty] public CompanySettings Input { get; set; } = new();
    [BindProperty] public EstimateDefaults Estimates { get; set; } = new();

    public async Task OnGetAsync()
    {
        Input = await settings.GetAsync();
        Estimates = await settings.EstimateDefaultsAsync();
    }

    public Task<IActionResult> OnPostAsync() => Guard(async () =>
    {
        Estimates = await settings.EstimateDefaultsAsync(); // the other form, for redisplay
        await settings.SaveAsync(Input);
        Flash("Settings saved.");
        return RedirectToPage();
    }, Page);

    public Task<IActionResult> OnPostEstimatesAsync() => Guard(async () =>
    {
        Input = await settings.GetAsync();
        ModelState.Clear();
        await settings.SaveEstimateDefaultsAsync(Estimates);
        Flash("Estimate defaults saved. New estimates will use them.");
        return RedirectToPage();
    }, Page);
}
