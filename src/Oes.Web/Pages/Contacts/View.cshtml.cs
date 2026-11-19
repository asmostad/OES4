using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Contacts;

public class ViewModel(ContactService contacts, PropertyService properties, RecordItems items, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public ContactDetail Detail { get; private set; } = null!;
    public TimelineModel Timeline { get; private set; } = null!;
    public List<Option> Roles { get; private set; } = [];
    public string? SourceLabel { get; private set; }

    public Task<IActionResult> OnGetAsync(string? tab, string? range) => Guard(async () =>
    {
        Detail = await contacts.GetAsync(Id);
        Timeline = await TimelineModel.LoadAsync(items, lookups, EntityTypes.Contact, Id, tab, range);
        Roles = await lookups.ValuesAsync(LookupTypes.PropertyContactRole);
        SourceLabel = await lookups.LabelAsync(LookupTypes.LeadSource, Detail.Contact.SourceKey);
        return Page();
    });

    public Task<IActionResult> OnPostLinkPropertyAsync(Guid propertyId, string roleKey) => Guard(async () =>
    {
        await properties.LinkContactAsync(propertyId, Id, roleKey, isPrimary: false);
        Flash("Property linked.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostEndPropertyLinkAsync(Guid linkId) => Guard(async () =>
    {
        await properties.EndContactLinkAsync(linkId);
        Flash("Property link ended. It stays in the history.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostAddToOrganizationAsync(Guid organizationId, string? title) => Guard(async () =>
    {
        await contacts.AddToOrganizationAsync(Id, organizationId, title);
        Flash("Added to organization.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostAddPersonAsync(Guid personId, string? title) => Guard(async () =>
    {
        await contacts.AddToOrganizationAsync(personId, Id, title);
        Flash("Person added.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostSaveAddressAsync(string kind, AddressInput address) => Guard(async () =>
    {
        await contacts.SaveAddressAsync(Id, kind, address);
        Flash("Address saved.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostRemoveAddressAsync(string kind) => Guard(async () =>
    {
        await contacts.RemoveAddressAsync(Id, kind);
        Flash("Address removed.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostEndRelationshipAsync(Guid relationshipId) => Guard(async () =>
    {
        await contacts.EndRelationshipAsync(relationshipId);
        Flash("Link ended.");
        return RedirectToPage(new { id = Id });
    });
}
