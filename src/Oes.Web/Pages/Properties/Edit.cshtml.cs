using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Properties;

public class EditModel(PropertyService properties) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public PropertyInput Input { get; set; } = new();
    public string Title { get; private set; } = "";

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        var p = (await properties.GetAsync(Id)).Property;
        Input = new PropertyInput { Line1 = p.Line1, Line2 = p.Line2, City = p.City, State = p.State, PostalCode = p.PostalCode, Kind = p.Kind, Notes = p.Notes };
        Title = p.Line1;
        return Page();
    });

    public Task<IActionResult> OnPostAsync() => Guard(async () =>
    {
        Title = Input.Line1;
        try { await properties.UpdateAsync(Id, Input); }
        catch (DuplicatePropertyException dup)
        {
            ModelState.AddModelError("", dup.Message + " Use the merge tool (coming soon) or pick a different address.");
            return Page();
        }
        Flash("Property saved.");
        return RedirectToPage("/Properties/View", new { id = Id });
    }, Page);
}
