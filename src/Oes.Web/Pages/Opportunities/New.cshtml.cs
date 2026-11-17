using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Opportunities;

/// <summary>
/// Lead intake. The contact can be existing or new; the address is optional (most leads
/// arrive without one) and, when given, reuses an existing property instead of duplicating it.
/// </summary>
[HasPermission(Permissions.RecordsCreate)]
public class NewModel(OpportunityService opportunities, ContactService contacts, PropertyService properties, CrmLookups lookups, RecordScope scope,
    Oes.Infrastructure.Insurance.StormService storms)
    : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? ContactId { get; set; }
    [BindProperty] public string? ContactLabel { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? PropertyId { get; set; }
    [BindProperty] public string? PropertyLabel { get; set; }

    [BindProperty, StringLength(100)] public string? FirstName { get; set; }
    [BindProperty, StringLength(100)] public string? LastName { get; set; }
    [BindProperty, StringLength(254), EmailAddress] public string? Email { get; set; }
    [BindProperty, StringLength(40)] public string? Phone { get; set; }

    [BindProperty] public PropertyInput Address { get; set; } = new();
    [BindProperty] public OpportunityInput Input { get; set; } = new();

    public List<Option> Users { get; private set; } = [];
    public List<Option> Sources { get; private set; } = [];
    public List<Option> Perils { get; private set; } = [];
    public List<Oes.Infrastructure.Insurance.StormRow> Storms { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public Guid? StormId { get; set; }
    public List<PipelineStage> Stages { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Input.OwnerUserId = scope.Me;
        if (StormId is { } stormId)
        {
            // Coming from a storm's page or canvass list: it's an insurance lead on that storm.
            var storm = await storms.GetAsync(stormId);
            Input.IsInsurance = true;
            Input.StormEventId = storm.Id;
            Input.PerilKey = storm.PerilKey;
            Input.DateOfLoss = storm.EventDate;
            Input.SourceKey = "storm_canvass";
        }
        if (ContactId is { } cid) ContactLabel = (await contacts.GetAsync(cid)).Contact.DisplayName;
        if (PropertyId is { } pid)
        {
            var detail = await properties.GetAsync(pid);
            PropertyLabel = detail.Property.SingleLine;
            // Starting from a property: default to its primary contact.
            var primary = detail.Contacts.FirstOrDefault(c => c.IsCurrent && c.IsPrimary) ?? detail.Contacts.FirstOrDefault(c => c.IsCurrent);
            if (ContactId is null && primary is not null) { ContactId = primary.ContactId; ContactLabel = primary.Name; }
        }
        await LoadAsync();
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!ModelState.IsValid) return Page();
        if (ContactId is null && string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName))
        {
            ModelState.AddModelError("", "Pick an existing contact or enter a name for a new one.");
            return Page();
        }

        return await Guard(async () =>
        {
            // Property first (standalone), then contact, then the opportunity tying them together.
            var propertyId = PropertyId;
            if (propertyId is null && !string.IsNullOrWhiteSpace(Address.Line1))
            {
                try { propertyId = await properties.CreateAsync(Address); }
                catch (DuplicatePropertyException dup)
                {
                    if (!await scope.CanSeeAsync(EntityTypes.Property, dup.ExistingId))
                        throw new CrmRuleException($"{dup.Message} It belongs to another rep; ask a manager to reassign it.");
                    propertyId = dup.ExistingId;
                }
            }

            var contactId = ContactId ?? await contacts.CreateAsync(new ContactInput
            {
                FirstName = FirstName, LastName = LastName, OwnerUserId = Input.OwnerUserId, SourceKey = Input.SourceKey,
                Methods =
                [
                    .. string.IsNullOrWhiteSpace(Email) ? Array.Empty<MethodInput>() : [new MethodInput(ContactMethodKinds.Email, Email, null, true)],
                    .. string.IsNullOrWhiteSpace(Phone) ? Array.Empty<MethodInput>() : [new MethodInput(ContactMethodKinds.Mobile, Phone, null, true)],
                ],
            });

            Input.PrimaryContactId = contactId;
            Input.PropertyId = propertyId;
            var id = await opportunities.CreateAsync(Input);
            Flash("Opportunity created.");
            return RedirectToPage("/Opportunities/View", new { id });
        }, Page);
    }

    private async Task LoadAsync()
    {
        Users = await lookups.UsersAsync();
        Sources = await lookups.ValuesAsync(LookupTypes.LeadSource);
        Perils = await lookups.ValuesAsync(LookupTypes.Peril);
        Storms = await storms.ListAsync();
        Stages = (await opportunities.StagesAsync()).Where(s => s.Outcome == Outcomes.Open).ToList();
    }
}
