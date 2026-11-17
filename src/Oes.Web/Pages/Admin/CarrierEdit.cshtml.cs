using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Insurance;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

/// <summary>
/// Edit a default carrier (stored as the tenant's overrides) or a tenant-only carrier.
/// choice = "p:{carrierId}" or "t:{tenantCarrierId}"; no choice = new tenant-only carrier.
/// </summary>
[HasPermission(Permissions.CarriersManage)]
public class CarrierEditModel(CarrierService carriers) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Choice { get; set; }
    [BindProperty] public CarrierInput Input { get; set; } = new();

    public EffectiveCarrier? Current { get; private set; }
    public bool IsDefault => Current is { IsCustom: false };

    public async Task<IActionResult> OnGetAsync()
    {
        if (Choice is not null)
        {
            Current = await FindAsync();
            if (Current is null) return NotFound();
            Input = new CarrierInput
            {
                Name = Current.Name, Phone = Current.Phone, ClaimsEmail = Current.ClaimsEmail, ClaimsFax = Current.ClaimsFax,
                Website = Current.Website, PreferredSubmission = Current.PreferredSubmission, Address = Current.Address, Notes = Current.Notes,
            };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Current = Choice is null ? null : await FindAsync();
        return await Guard(async () =>
        {
            if (Current is { IsCustom: false, CarrierId: { } carrierId }) await carriers.SaveOverridesAsync(carrierId, Input);
            else await carriers.SaveCustomAsync(Current?.TenantCarrierId, Input);
            Flash("Carrier saved.");
            return RedirectToPage("/Admin/Carriers");
        }, Page);
    }

    private async Task<EffectiveCarrier?> FindAsync() =>
        (await carriers.ListAsync(includeHidden: true)).FirstOrDefault(c => CarrierService.ChoiceFor(c) == Choice
            || (Choice!.StartsWith("p:") && c.CarrierId?.ToString() == Choice[2..]));
}
