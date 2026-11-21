using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Estimating;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

/// <summary>Upload a carrier scope PDF; AI reads it into lines for review. Earlier imports are listed.</summary>
[RequestSizeLimit(60 * 1024 * 1024)]
public class ImportScopeModel(ScopeImportService imports, EstimateService estimates) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public IFormFile? Scope { get; set; }

    public EstimateDetail Detail { get; private set; } = null!;
    public List<ScopeImport> Imports { get; private set; } = [];
    public bool AiReady => imports.IsConfigured;
    public bool CanUseScopes { get; private set; }

    private async Task LoadAsync()
    {
        Detail = await estimates.GetAsync(Id);
        CanUseScopes = await estimates.HasEditRightsAsync(Detail.Estimate);
        Imports = await imports.ForEstimateAsync(Id);
    }

    public Task<IActionResult> OnGetAsync() => Guard(async () => { await LoadAsync(); return Page(); });

    public Task<IActionResult> OnPostAsync() => Guard(async () =>
    {
        await LoadAsync();
        if (Scope is not { Length: > 0 })
        {
            ModelState.AddModelError("", "Attach the carrier's scope PDF first.");
            return Page();
        }
        await using var stream = Scope.OpenReadStream();
        var importId = await imports.ExtractAsync(Id, stream, Scope.FileName);
        return RedirectToPage("/Estimates/ScopeReview", new { id = importId });
    }, Page);
}
