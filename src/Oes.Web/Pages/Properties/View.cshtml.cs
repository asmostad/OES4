using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Properties;

public class ViewModel(PropertyService properties, RecordItems items, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public PropertyDetail Detail { get; private set; } = null!;
    public TimelineModel Timeline { get; private set; } = null!;
    public List<Option> Roles { get; private set; } = [];

    public Task<IActionResult> OnGetAsync(string? tab, string? range) => Guard(async () =>
    {
        Detail = await properties.GetAsync(Id);
        Timeline = await TimelineModel.LoadAsync(items, lookups, EntityTypes.Property, Id, tab, range);
        Roles = await lookups.ValuesAsync(LookupTypes.PropertyContactRole);
        return Page();
    });

    public Task<IActionResult> OnPostLinkContactAsync(Guid contactId, string roleKey, bool isPrimary) => Guard(async () =>
    {
        await properties.LinkContactAsync(Id, contactId, roleKey, isPrimary);
        Flash("Contact linked.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostEndLinkAsync(Guid linkId) => Guard(async () =>
    {
        await properties.EndContactLinkAsync(linkId);
        Flash("Link ended. It stays in the property's history.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostAddStructureAsync(string name) => Guard(async () =>
    {
        await properties.AddStructureAsync(Id, name);
        Flash("Structure added.");
        return RedirectToPage(new { id = Id });
    });
}
