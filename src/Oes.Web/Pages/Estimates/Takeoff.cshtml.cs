using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Estimating;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

/// <summary>Run the takeoff rules on one estimate section: pick products per slot and options, preview, then apply.</summary>
public class TakeoffModel(TakeoffService takeoff, EstimateService estimates) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid Section { get; set; }
    [BindProperty] public TakeoffChoicesInput Choices { get; set; } = new();

    public SectionTakeoff Run { get; private set; } = null!;
    public string EstimateLabel { get; private set; } = "";
    public bool Previewed { get; private set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        await LoadAsync(null);
        return Page();
    });

    public Task<IActionResult> OnPostPreviewAsync() => Guard(async () =>
    {
        await LoadAsync(Choices);
        Previewed = true;
        return Page();
    });

    public Task<IActionResult> OnPostApplyAsync() => Guard(async () =>
    {
        await LoadAsync(Choices); // for redisplay if it's refused
        var n = await takeoff.ApplyAsync(Id, Section, Choices);
        Flash($"Takeoff added {n} line{(n == 1 ? "" : "s")}. Lines you added by hand were left alone.");
        return Redirect($"/Estimates/View/{Id}#s-{Section:N}");
    }, Page);

    private async Task LoadAsync(TakeoffChoicesInput? choices)
    {
        Run = await takeoff.ForSectionAsync(Id, Section, choices);
        var est = (await estimates.GetAsync(Id)).Estimate;
        EstimateLabel = $"{est.Number} {est.Name}";
        Choices = Run.Choices;
    }
}
