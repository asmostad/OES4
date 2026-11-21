using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Contacts;

public class EditModel(ContactService contacts, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public ContactForm Form { get; set; } = new();

    public string DisplayName { get; private set; } = "";
    public List<Option> Users { get; private set; } = [];
    public List<Option> Sources { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        var detail = await contacts.GetAsync(Id);
        if (!detail.CanEdit) return Forbid();
        Form = ContactForm.From(detail.Contact);
        DisplayName = detail.Contact.DisplayName;
        Form.PadMethods();
        await LoadAsync();
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (!ModelState.IsValid) return Redisplay();
        return await Guard(async () =>
        {
            await contacts.UpdateAsync(Id, Form.ToInput());
            Flash("Contact saved.");
            return RedirectToPage("/Contacts/View", new { id = Id });
        }, Redisplay);
    }

    public Task<IActionResult> OnPostArchiveAsync() => Guard(async () =>
    {
        await contacts.ArchiveAsync(Id);
        Flash("Contact archived.");
        return RedirectToPage("/Contacts/Index");
    });

    private IActionResult Redisplay()
    {
        DisplayName = Form.IsOrganization ? Form.OrganizationName ?? "" : $"{Form.FirstName} {Form.LastName}".Trim();
        Form.Methods = Form.Methods.Where(m => !string.IsNullOrWhiteSpace(m.Value)).ToList();
        Form.PadMethods();
        return Page();
    }

    private async Task LoadAsync()
    {
        Users = await lookups.UsersAsync();
        Sources = await lookups.ValuesAsync(LookupTypes.LeadSource);
    }
}
