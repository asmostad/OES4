using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Opportunities;

public class ViewModel(OpportunityService opportunities, RecordItems items, CrmLookups lookups,
    Oes.Infrastructure.Insurance.ClaimService claims, Oes.Infrastructure.Insurance.StormService storms) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public OpportunityDetail Detail { get; private set; } = null!;
    public TimelineModel Timeline { get; private set; } = null!;
    public List<Option> LostReasons { get; private set; } = [];
    public string? SourceLabel { get; private set; }
    public string? PerilLabel { get; private set; }
    public string? LostReasonLabel { get; private set; }
    public Oes.Infrastructure.Insurance.ClaimDetail? Claim { get; private set; }
    public Oes.Domain.Insurance.StormEvent? Storm { get; private set; }
    public Oes.Domain.Insurance.StormEvent? SuggestedStorm { get; private set; }

    public Task<IActionResult> OnGetAsync(string? tab) => Guard(async () =>
    {
        Detail = await opportunities.GetAsync(Id);
        Timeline = await TimelineModel.LoadAsync(items, lookups, EntityTypes.Opportunity, Id, tab, null);
        LostReasons = await lookups.ValuesAsync(LookupTypes.LostReason);
        SourceLabel = await lookups.LabelAsync(LookupTypes.LeadSource, Detail.Opportunity.SourceKey);
        PerilLabel = await lookups.LabelAsync(LookupTypes.Peril, Detail.Opportunity.PerilKey);
        LostReasonLabel = await lookups.LabelAsync(LookupTypes.LostReason, Detail.Opportunity.LostReasonKey);
        if (await claims.ForOpportunityAsync(Id) is { } claimId) Claim = await claims.GetAsync(claimId);
        var o = Detail.Opportunity;
        if (o.StormEventId is { } stormId) Storm = await storms.GetAsync(stormId);
        else if (o.IsInsurance && Detail.Property is not null)
            SuggestedStorm = await storms.SuggestAsync(Detail.Property.PostalCode, o.DateOfLoss);
        return Page();
    });

    public Task<IActionResult> OnPostStageAsync(Guid stageId, string? note, string? lostReasonKey) => Guard(async () =>
    {
        await opportunities.MoveStageAsync(Id, stageId, note, lostReasonKey);
        Flash("Stage updated.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostAttachPropertyAsync(Guid propertyId) => Guard(async () =>
    {
        await opportunities.AttachPropertyAsync(Id, propertyId);
        Flash("Property attached. Notes and files now roll up to it.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostAttachNewPropertyAsync(PropertyInput address) => Guard(async () =>
    {
        await opportunities.AttachNewPropertyAsync(Id, address);
        Flash("Property attached.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostAttemptAsync(string? note) => Guard(async () =>
    {
        await opportunities.RecordContactAttemptAsync(Id, note);
        Flash("Contact attempt logged.");
        return RedirectToPage(new { id = Id });
    });
}
