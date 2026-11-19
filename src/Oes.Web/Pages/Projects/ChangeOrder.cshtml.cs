using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Production;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Projects;

/// <summary>A change order: its items while it's a draft, the customer's decision, and a printable copy (?print=true).</summary>
public class ChangeOrderModel(ProjectService projects, OesDbContext db, ITenantContext tenant) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool Print { get; set; }

    public ChangeOrderDetail Detail { get; private set; } = null!;
    public Tenant Company { get; private set; } = null!;

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Detail = await projects.GetChangeOrderAsync(Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        return Page();
    });

    public Task<IActionResult> OnPostPriceItemAsync(Guid priceListItemId, decimal qty) => Do(() => projects.AddChangeOrderPriceItemAsync(Id, priceListItemId, qty));

    public Task<IActionResult> OnPostProductAsync(Guid productId, decimal qty) => Do(() => projects.AddChangeOrderProductAsync(Id, productId, qty));

    public Task<IActionResult> OnPostCustomAsync(string description, decimal qty, string? unit, decimal unitPrice) =>
        Do(() => projects.AddChangeOrderCustomLineAsync(Id, description, qty, unit, unitPrice));

    public Task<IActionResult> OnPostUpdateLineAsync(Guid lineId, decimal qty, decimal unitPrice, string? description) =>
        Do(() => projects.UpdateChangeOrderLineAsync(lineId, qty, unitPrice, description));

    public Task<IActionResult> OnPostRemoveLineAsync(Guid lineId) => Do(() => projects.RemoveChangeOrderLineAsync(lineId));

    public Task<IActionResult> OnPostDecideAsync(bool approve, string? approvedByName) => Guard(async () =>
    {
        await projects.DecideChangeOrderAsync(Id, approve, approvedByName);
        Flash(approve ? "Change order approved and added to the contract." : "Change order rejected.");
        return RedirectToPage(new { id = Id });
    }, StayHere);

    private Task<IActionResult> Do(Func<Task> action) => Guard(async () =>
    {
        await action();
        return RedirectToPage(new { id = Id });
    }, StayHere);

    private IActionResult StayHere()
    {
        var errors = ModelState[""]?.Errors.Select(e => e.ErrorMessage).ToList();
        if (errors is { Count: > 0 }) Flash(string.Join(" ", errors), "danger");
        return RedirectToPage(new { id = Id });
    }
}
