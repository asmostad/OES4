using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Opportunities;

public class EditModel(OpportunityService opportunities, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public OpportunityInput Input { get; set; } = new();
    public string Title { get; private set; } = "";
    public string Number { get; private set; } = "";
    public List<Option> Users { get; private set; } = [];
    public List<Option> Sources { get; private set; } = [];
    public List<Option> Perils { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        var d = await opportunities.GetAsync(Id);
        if (!d.CanEdit) return Forbid();
        var o = d.Opportunity;
        Input = new OpportunityInput
        {
            Name = o.Name, OwnerUserId = o.OwnerUserId, SourceKey = o.SourceKey, ExpectedValue = o.ExpectedValue,
            IsInsurance = o.IsInsurance, PerilKey = o.PerilKey, DateOfLoss = o.DateOfLoss, Description = o.Description,
            PrimaryContactId = o.PrimaryContactId, PropertyId = o.PropertyId,
        };
        Title = o.Name;
        Number = o.Number;
        await LoadAsync();
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        Title = Input.Name ?? "";
        return await Guard(async () =>
        {
            await opportunities.UpdateAsync(Id, Input);
            Flash("Opportunity saved.");
            return RedirectToPage("/Opportunities/View", new { id = Id });
        }, Page);
    }

    public Task<IActionResult> OnPostArchiveAsync() => Guard(async () =>
    {
        await opportunities.ArchiveAsync(Id);
        Flash("Opportunity archived.");
        return RedirectToPage("/Opportunities/Index");
    });

    private async Task LoadAsync()
    {
        Users = await lookups.UsersAsync();
        Sources = await lookups.ValuesAsync(LookupTypes.LeadSource);
        Perils = await lookups.ValuesAsync(LookupTypes.Peril);
    }
}
