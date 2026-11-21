using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Records;

/// <summary>Merge a duplicate contact or property into this one: pick the duplicate, see what moves, confirm.</summary>
[HasPermission(Permissions.RecordsMerge)]
public class MergeModel(MergeService merges, PropertyService properties, ContactService contacts) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string Type { get; set; } = "";
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Other { get; set; }

    public bool IsContact => Type == EntityTypes.Contact;
    public string Noun => IsContact ? "contact" : "property";
    public string KeepName { get; private set; } = "";
    public string BackUrl => IsContact ? $"/Contacts/View/{Id}" : $"/Properties/View/{Id}";
    public List<(Guid Id, string Name, string Why)> Suggestions { get; private set; } = [];
    public MergePreview? Preview { get; private set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Type is not (EntityTypes.Contact or EntityTypes.Property)) return NotFound();
        KeepName = IsContact ? (await contacts.GetAsync(Id)).Contact.DisplayName : (await properties.GetAsync(Id)).Property.SingleLine;
        if (Other is { } other && other != Id)
            Preview = IsContact ? await merges.PreviewContactsAsync(Id, other) : await merges.PreviewPropertiesAsync(Id, other);
        else
            Suggestions = IsContact ? await merges.SuggestContactsAsync(Id) : await merges.SuggestPropertiesAsync(Id, properties);
        return Page();
    });

    public Task<IActionResult> OnPostAsync() => Guard(async () =>
    {
        if (Other is not { } other) return RedirectToPage(new { Type, Id });
        if (IsContact) await merges.MergeContactsAsync(Id, other);
        else if (Type == EntityTypes.Property) await merges.MergePropertiesAsync(Id, other);
        else return NotFound();
        Flash($"Merged. Everything from the duplicate {Noun} is on this one now.");
        return Redirect(BackUrl);
    }); // a refusal is flashed and lands back on this page
}
