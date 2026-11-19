using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Prospecting;

/// <summary>
/// One lead at a time. Without an id it claims the next due lead and redirects to it, so the URL
/// always names the lead on screen. "Skip" keeps the lead out of this run (ids carried in the query string).
/// </summary>
[HasPermission(Permissions.LeadsProspect)]
public class WorkModel(ProspectingService prospecting, TenantTime time) : ProspectingPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Skip { get; set; }

    public ProspectCard? Card { get; private set; }
    public bool HaveLock { get; private set; }
    public Guid? Previous { get; private set; }
    public List<Disposition> Dispositions { get; private set; } = [];
    public DateOnly Today { get; private set; }

    private List<Guid> Skipped => (Skip ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().Take(100).ToList();

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Id is null)
        {
            var next = await prospecting.NextAsync(Filter, Skipped);
            if (next is { } n) return RedirectToPage(FilterRoute(n, Skip));
            Previous = await prospecting.PreviousAsync(null);
            return Page();
        }
        HaveLock = await prospecting.TryLockAsync(Id.Value);
        Card = await prospecting.CardAsync(Id.Value);
        Previous = await prospecting.PreviousAsync(Id);
        Dispositions = await prospecting.DispositionsAsync();
        Today = await time.TodayAsync();
        return Page();
    });

    public Task<IActionResult> OnPostDispositionAsync(Guid dispositionId, string? note, DateOnly? callBackOn) => Guard(async () =>
    {
        await prospecting.ApplyAsync(Id!.Value, dispositionId, note, callBackOn);
        Flash($"Saved for {(await prospecting.CardAsync(Id.Value)).Contact.DisplayName}.");
        return RedirectToPage(FilterRoute(null, Skip));
    }, StayHere);

    public Task<IActionResult> OnPostSkipAsync() => Guard(async () =>
    {
        await prospecting.ReleaseAsync(Id!.Value);
        var skip = string.Join(",", Skipped.Append(Id.Value).Distinct().TakeLast(100).Select(g => g.ToString("N")));
        return RedirectToPage(FilterRoute(null, skip));
    });

    public Task<IActionResult> OnPostStopAsync() => Guard(async () =>
    {
        if (Id is { } id) await prospecting.ReleaseAsync(id);
        return RedirectToPage("/Prospecting/Index", FilterRoute());
    });

    private IActionResult StayHere()
    {
        var errors = ModelState[""]?.Errors.Select(e => e.ErrorMessage).ToList();
        if (errors is { Count: > 0 }) Flash(string.Join(" ", errors), "danger");
        return RedirectToPage(FilterRoute(Id, Skip));
    }
}
