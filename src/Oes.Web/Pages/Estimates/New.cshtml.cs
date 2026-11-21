using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Estimates;

[HasPermission(Permissions.EstimatesCreate)]
public class NewModel(EstimateService estimates, CatalogService catalog, OpportunityService opportunities) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid OpportunityId { get; set; }
    [BindProperty] public NewEstimateInput Input { get; set; } = new();

    public OpportunityDetail Opportunity { get; private set; } = null!;
    public List<PriceListRow> PriceLists { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        await LoadAsync();
        Input.Name = Opportunity.Opportunity.Name;
        Input.PriceListId = PriceLists.FirstOrDefault()?.List.Id;
        if (PriceLists.Count == 0) Input.Method = EstimateMethods.LineItem;
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        return await Guard(async () =>
        {
            Input.OpportunityId = OpportunityId;
            var id = await estimates.CreateAsync(Input);
            return RedirectToPage("/Estimates/View", new { id });
        }, Page);
    }

    private async Task LoadAsync()
    {
        Opportunity = await opportunities.GetAsync(OpportunityId);
        PriceLists = (await catalog.PriceListsAsync()).Where(p => p.List.Status == PriceListStatuses.Active).ToList();
    }
}
