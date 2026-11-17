using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Contacts;

[HasPermission(Permissions.RecordsCreate)]
public class NewModel(ContactService contacts, PropertyService properties, CrmLookups lookups, RecordScope scope) : CrmPageModel
{
    [BindProperty] public ContactForm Form { get; set; } = new();
    [BindProperty] public PropertyInput Address { get; set; } = new();
    [BindProperty] public string? AddressRole { get; set; } = "owner";
    [BindProperty] public bool CreateAnyway { get; set; }

    public List<Option> Users { get; private set; } = [];
    public List<Option> Sources { get; private set; } = [];
    public List<Option> Roles { get; private set; } = [];
    public List<(Guid Id, string Name)> Duplicates { get; private set; } = [];

    public async Task OnGetAsync(string? type, Guid? organizationId)
    {
        Form.Type = type == ContactTypes.Organization ? ContactTypes.Organization : ContactTypes.Person;
        Form.OwnerUserId = scope.Me;
        if (organizationId is { } orgId)
        {
            var org = (await lookups.OrganizationsAsync(null)).FirstOrDefault(o => o.Value == orgId.ToString());
            if (org is not null) { Form.OrganizationId = orgId; Form.OrganizationLabel = org.Label; }
        }
        Form.PadMethods();
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!ModelState.IsValid) return Redisplay();

        if (!CreateAnyway)
        {
            Duplicates = await contacts.FindMatchesAsync(Form.ToInput().Methods);
            if (Duplicates.Count > 0) return Redisplay();
        }

        return await Guard(async () =>
        {
            // Property first: it's a standalone root record, so if the contact then fails nothing is half-made.
            Guid? propertyId = null;
            if (!string.IsNullOrWhiteSpace(Address.Line1))
            {
                try { propertyId = await properties.CreateAsync(Address); }
                catch (DuplicatePropertyException dup)
                {
                    if (!await scope.CanSeeAsync(EntityTypes.Property, dup.ExistingId))
                        throw new CrmRuleException($"{dup.Message} It belongs to another rep; ask a manager to link this contact to it.");
                    propertyId = dup.ExistingId;
                }
            }
            var id = await contacts.CreateAsync(Form.ToInput());
            if (propertyId is { } pid) await properties.LinkContactAsync(pid, id, AddressRole ?? "owner", isPrimary: false);
            Flash($"{(Form.IsOrganization ? "Organization" : "Contact")} created.");
            return RedirectToPage("/Contacts/View", new { id });
        }, Redisplay);
    }

    private IActionResult Redisplay()
    {
        Form.Methods = Form.Methods.Where(m => !string.IsNullOrWhiteSpace(m.Value)).ToList();
        Form.PadMethods();
        return Page();
    }

    private async Task LoadAsync()
    {
        Users = await lookups.UsersAsync();
        Sources = await lookups.ValuesAsync(LookupTypes.LeadSource);
        Roles = await lookups.ValuesAsync(LookupTypes.PropertyContactRole);
    }
}
