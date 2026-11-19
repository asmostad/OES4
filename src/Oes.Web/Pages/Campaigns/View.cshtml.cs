using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Campaigns;

[HasPermission(Permissions.LeadsCampaigns)]
public class ViewModel(CampaignService campaigns, OpportunityService opportunities, CrmLookups lookups,
    Oes.Infrastructure.Insurance.StormService storms) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public CampaignDetail Detail { get; private set; } = null!;
    public List<string> CriteriaText { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Detail = await campaigns.GetAsync(Id);
        var c = Detail.Criteria;
        if (c.StageId is { } sid) CriteriaText.Add("Stage: " + ((await opportunities.StagesAsync()).FirstOrDefault(s => s.Id == sid)?.Name ?? "(removed)"));
        if (c.SourceKey is not null) CriteriaText.Add("Lead source: " + (await lookups.LabelAsync(Oes.Domain.Crm.LookupTypes.LeadSource, c.SourceKey) ?? c.SourceKey));
        if (c.StormId is { } storm) CriteriaText.Add("Storm: " + ((await storms.ListAsync(true)).FirstOrDefault(s => s.Id == storm)?.Name ?? "(removed)"));
        if (c.PostalCodes.Count > 0) CriteriaText.Add("ZIP codes: " + string.Join(", ", c.PostalCodes));
        if (c.MaxAttempts is { } max) CriteriaText.Add($"At most {max} attempt{(max == 1 ? "" : "s")}");
        if (c.CreatedFrom is not null || c.CreatedTo is not null)
            CriteriaText.Add("Created " + (c.CreatedFrom is { } f ? "from " + f.ToString("MMM d, yyyy") + " " : "") + (c.CreatedTo is { } t ? "to " + t.ToString("MMM d, yyyy") : "").Trim());
        if (c.UnassignedOnly) CriteriaText.Add("Unassigned leads only");
        if (CriteriaText.Count == 0) CriteriaText.Add("Every open lead");
        return Page();
    });

    public Task<IActionResult> OnPostAddMatchesAsync() => Guard(async () =>
    {
        var n = await campaigns.AddMatchesAsync(Id);
        Flash(n == 0 ? "No new leads match." : $"Added {n} lead{(n == 1 ? "" : "s")}.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostRemoveAsync(Guid opportunityId) => Guard(async () =>
    {
        await campaigns.RemoveLeadAsync(Id, opportunityId);
        Flash("Removed from the campaign.");
        return RedirectToPage(new { id = Id });
    });
}
