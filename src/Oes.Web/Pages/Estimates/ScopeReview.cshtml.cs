using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Ai;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

/// <summary>Review the lines the AI read from a carrier scope, then add the ticked ones to the estimate.</summary>
public class ScopeReviewModel(ScopeImportService imports) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public List<int> Include { get; set; } = [];
    [BindProperty] public Dictionary<int, decimal> Quantity { get; set; } = [];
    [BindProperty] public string? SectionName { get; set; }
    /// <summary>Unmatched lines to add as the suggested price list item instead of a custom line.</summary>
    [BindProperty] public List<int> UseSuggestion { get; set; } = [];

    public ScopeImportView View { get; private set; } = null!;

    public Task<IActionResult> OnGetAsync() => Guard(async () => { View = await imports.GetAsync(Id); return Page(); });

    public Task<IActionResult> OnPostAsync() => Guard(async () =>
    {
        View = await imports.GetAsync(Id);
        var added = await imports.ApplyAsync(Id, Include, Quantity, SectionName ?? "", UseSuggestion);
        Flash($"Added {added} line{(added == 1 ? "" : "s")} from the carrier scope. Custom lines need approval as usual.");
        return Redirect($"/Estimates/View/{View.Estimate.Id}");
    }, Page);

    public Task<IActionResult> OnPostDiscardAsync() => Guard(async () =>
    {
        View = await imports.GetAsync(Id);
        await imports.DiscardAsync(Id);
        Flash("Scope import discarded. The PDF stays on the estimate.");
        return Redirect($"/Estimates/View/{View.Estimate.Id}");
    }, Page);
}
