using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Campaigns;

[HasPermission(Permissions.LeadsCampaigns)]
public class EditModel(CampaignService campaigns, OpportunityService opportunities, CrmLookups lookups,
    Oes.Infrastructure.Insurance.StormService storms) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public CampaignInput Input { get; set; } = new();
    /// <summary>ZIP codes as typed (comma or space separated).</summary>
    [BindProperty] public string? Zips { get; set; }

    public int? Preview { get; private set; }
    public List<PipelineStage> Stages { get; private set; } = [];
    public List<Option> Sources { get; private set; } = [];
    public List<Oes.Infrastructure.Insurance.StormRow> Storms { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Id is { } id)
        {
            var d = await campaigns.GetAsync(id);
            Input = new CampaignInput
            {
                Id = id, Name = d.Campaign.Name, Description = d.Campaign.Description, Mode = d.Campaign.Mode, IsActive = d.Campaign.IsActive, Criteria = d.Criteria,
            };
            Zips = string.Join(", ", d.Criteria.PostalCodes);
        }
        await LoadAsync();
        return Page();
    });

    public Task<IActionResult> OnPostPreviewAsync() => Guard(async () =>
    {
        Prepare();
        Preview = await campaigns.PreviewAsync(Input.Criteria);
        await LoadAsync();
        return Page();
    });

    public Task<IActionResult> OnPostAsync() => Guard(async () =>
    {
        Prepare();
        await LoadAsync(); // for redisplay if the save is refused
        var id = await campaigns.SaveAsync(Input);
        Flash(Id is null ? "Campaign created." : "Campaign saved.");
        return RedirectToPage("/Campaigns/View", new { id });
    }, Page);

    private void Prepare()
    {
        ModelState.Clear();
        Input.Id = Id;
        Input.Criteria.PostalCodes = string.IsNullOrWhiteSpace(Zips) ? [] : [Zips];
    }

    private async Task LoadAsync()
    {
        Stages = await opportunities.StagesAsync();
        Sources = await lookups.ValuesAsync(LookupTypes.LeadSource);
        Storms = await storms.ListAsync();
    }
}
