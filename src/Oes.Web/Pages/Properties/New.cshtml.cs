using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Properties;

[HasPermission(Permissions.RecordsCreate)]
public class NewModel(PropertyService properties, ContactService contacts, CrmLookups lookups) : CrmPageModel
{
    [BindProperty] public PropertyInput Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public Guid? ContactId { get; set; }
    [BindProperty] public string RoleKey { get; set; } = "owner";

    public string? ContactName { get; private set; }
    public List<Option> Roles { get; private set; } = [];
    public Guid? ExistingId { get; private set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () => { await LoadAsync(); return Page(); });

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        return await Guard(async () =>
        {
            Guid id;
            try { id = await properties.CreateAsync(Input); }
            catch (DuplicatePropertyException dup)
            {
                ExistingId = dup.ExistingId;
                ModelState.AddModelError("", dup.Message + " Open the existing property instead of creating a duplicate.");
                return Page();
            }
            if (ContactId is { } cid) await properties.LinkContactAsync(id, cid, RoleKey, isPrimary: true);
            Flash("Property created.");
            return RedirectToPage("/Properties/View", new { id });
        }, Page);
    }

    private async Task LoadAsync()
    {
        Roles = await lookups.ValuesAsync(LookupTypes.PropertyContactRole);
        if (ContactId is { } cid) ContactName = (await contacts.GetAsync(cid)).Contact.DisplayName;
    }
}
