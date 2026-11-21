using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Insurance;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Claims;

/// <summary>Start a claim from an opportunity (?opportunityId=) or edit one (/Claims/Edit/{id}).</summary>
[HasPermission(Permissions.ClaimsManage)]
public class EditModel(ClaimService claims, OpportunityService opportunities, CarrierService carriers, StormService storms, CrmLookups lookups)
    : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? OpportunityId { get; set; }
    [BindProperty] public ClaimInput Input { get; set; } = new();
    [BindProperty] public string? AdjusterLabel { get; set; }
    [BindProperty] public string? DeskAdjusterLabel { get; set; }

    public string OpportunityTitle { get; private set; } = "";
    public List<EffectiveCarrier> Carriers { get; private set; } = [];
    public List<StormRow> Storms { get; private set; } = [];
    public List<Option> Perils { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Id is { } id)
        {
            var d = await claims.GetAsync(id);
            if (!d.CanEdit) return Forbid();
            var c = d.Claim;
            OpportunityId = c.OpportunityId;
            OpportunityTitle = $"{d.Opportunity.Number} · {d.Opportunity.Name}";
            Input = new ClaimInput
            {
                CarrierChoice = d.Carrier is null ? null : CarrierService.ChoiceFor(d.Carrier),
                ClaimNumber = c.ClaimNumber, PolicyNumber = c.PolicyNumber, DateOfLoss = c.DateOfLoss, PerilKey = c.PerilKey,
                Deductible = c.Deductible, AdjusterContactId = c.AdjusterContactId, DeskAdjusterContactId = c.DeskAdjusterContactId,
                StormEventId = c.StormEventId, Status = c.Status, FiledOn = c.FiledOn, InspectionOn = c.InspectionOn,
                ApprovedAmount = c.ApprovedAmount, Notes = c.Notes,
            };
            AdjusterLabel = d.Adjuster?.DisplayName;
            DeskAdjusterLabel = d.DeskAdjuster?.DisplayName;
        }
        else if (OpportunityId is { } oid)
        {
            if (await claims.ForOpportunityAsync(oid) is { } existing) return RedirectToPage("/Claims/View", new { id = existing });
            var o = (await opportunities.GetAsync(oid)).Opportunity;
            OpportunityTitle = $"{o.Number} · {o.Name}";
            Input = new ClaimInput { DateOfLoss = o.DateOfLoss, PerilKey = o.PerilKey, StormEventId = o.StormEventId };
        }
        else return NotFound();
        await LoadAsync();
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        return await Guard(async () =>
        {
            Guid id;
            if (Id is { } existing) { await claims.UpdateAsync(existing, Input); id = existing; }
            else id = await claims.CreateAsync(OpportunityId ?? throw new CrmRuleException("Missing opportunity."), Input);
            Flash("Claim saved.");
            return RedirectToPage("/Claims/View", new { id });
        }, Page);
    }

    private async Task LoadAsync()
    {
        Carriers = await carriers.ListAsync();
        Storms = await storms.ListAsync();
        Perils = await lookups.ValuesAsync(LookupTypes.Peril);
        if (OpportunityTitle.Length == 0 && OpportunityId is { } oid)
        {
            var o = (await opportunities.GetAsync(oid)).Opportunity;
            OpportunityTitle = $"{o.Number} · {o.Name}";
        }
    }
}
