using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages;

/// <summary>Posts from the "Views" menu on list pages; every handler returns to the list.</summary>
public class SavedViewsModel(SavedViewService views) : CrmPageModel
{
    [BindProperty] public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => Redirect("/");

    public Task<IActionResult> OnPostSaveAsync(string listKey, string name, Dictionary<string, string?>? criteria, bool shared, bool isDefault) =>
        Run(async () =>
        {
            var id = await views.SaveAsync(listKey, name, criteria ?? [], shared, isDefault);
            Flash($"Saved the view \"{name.Trim()}\".");
            // Land on the view as saved (only its known filters).
            var row = (await views.ForListAsync(listKey)).FirstOrDefault(v => v.Id == id);
            return row is null ? null : row.Url;
        });

    public Task<IActionResult> OnPostDeleteAsync(Guid id) => Run(async () => { await views.DeleteAsync(id); Flash("View deleted."); return null; });

    public Task<IActionResult> OnPostDefaultAsync(Guid id, bool isDefault) => Run(async () =>
    {
        await views.SetDefaultAsync(id, isDefault);
        Flash(isDefault ? "The list now opens with this view." : "The list opens with no filters again.");
        return null;
    });

    private Task<IActionResult> Run(Func<Task<string?>> action) => Guard(async () => BackTo(await action() ?? ReturnUrl, "/"), Back);

    private IActionResult Back()
    {
        var errors = ModelState[""]?.Errors.Select(e => e.ErrorMessage).ToList();
        if (errors is { Count: > 0 }) Flash(string.Join(" ", errors), "danger");
        return BackTo(ReturnUrl, "/");
    }
}
