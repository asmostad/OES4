using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Measurements;

/// <summary>The property's measurements page: every structure × trade, current version first.</summary>
public class PropertyModel(MeasurementService measurements, PropertyService properties) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public PropertyMeasurements Data { get; private set; } = null!;
    public bool CanMeasure { get; private set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Data = await measurements.ForPropertyAsync(Id);
        CanMeasure = await measurements.CanMeasureAsync();
        return Page();
    });

    public Task<IActionResult> OnPostAddStructureAsync(string name) => Guard(async () =>
    {
        await properties.AddStructureAsync(Id, name);
        Flash("Structure added.");
        return RedirectToPage(new { id = Id });
    });
}
