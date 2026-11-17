using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Measurements;

public class SetModel(MeasurementService measurements) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public SetDetail Detail { get; private set; } = null!;

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Detail = await measurements.GetSetAsync(Id);
        return Page();
    });

    public Task<IActionResult> OnPostMakeCurrentAsync() => Guard(async () =>
    {
        await measurements.MakeCurrentAsync(Id);
        Flash("This version is current again.");
        return RedirectToPage(new { id = Id });
    });
}
