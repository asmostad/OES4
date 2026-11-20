using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Production;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Projects;

/// <summary>A trade's materials order: fill from the takeoff or the sold estimate, edit, print for the supplier (?print=true), mark ordered.</summary>
public class OrderSheetModel(OrderSheetService orders, OesDbContext db, ITenantContext tenant) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public bool Print { get; set; }
    [BindProperty] public TakeoffChoicesInput Choices { get; set; } = new();

    public OrderSheet Sheet { get; private set; } = null!;
    public Tenant Company { get; private set; } = null!;
    public bool Previewed { get; private set; }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        await LoadAsync(null);
        return Page();
    });

    /// <summary>The supplier copy as a PDF download.</summary>
    public Task<IActionResult> OnGetPdfAsync() => Guard(async () =>
    {
        await LoadAsync(null);
        Print = true;
        var name = $"{Sheet.Trade.Project.Number} {Sheet.Trade.Type.Name} order";
        return await PdfAsync(name, $"{Company.Name} · {name}", $"/Projects/OrderSheet/{Id}?print=true");
    });

    public Task<IActionResult> OnPostPreviewAsync() => Guard(async () =>
    {
        await LoadAsync(Choices);
        Previewed = true;
        return Page();
    });

    public Task<IActionResult> OnPostTakeoffAsync() => Done(async () =>
    {
        var n = await orders.FillFromTakeoffAsync(Id, Choices);
        Flash($"Added {n} line{(n == 1 ? "" : "s")} from the takeoff. Lines you added or changed were left alone.");
    });

    public Task<IActionResult> OnPostEstimateAsync() => Done(async () =>
    {
        var n = await orders.CopyFromEstimateAsync(Id);
        Flash($"Copied {n} line{(n == 1 ? "" : "s")} from the sold estimate.");
    });

    public Task<IActionResult> OnPostAddAsync(Guid? productId, string? description, decimal qty, string? unit, string? notes) =>
        Done(() => orders.AddLineAsync(Id, productId, description, qty, unit, notes));

    public Task<IActionResult> OnPostUpdateAsync(Guid lineId, decimal qty, string? notes) => Done(() => orders.UpdateLineAsync(lineId, qty, notes));

    public Task<IActionResult> OnPostRemoveAsync(Guid lineId) => Done(() => orders.RemoveLineAsync(lineId));

    public Task<IActionResult> OnPostOrderedAsync(bool ordered, string? supplier, string? orderNotes) => Done(async () =>
    {
        await orders.MarkOrderedAsync(Id, ordered, supplier, orderNotes);
        Flash(ordered ? "Marked as ordered." : "Order reopened.");
    });

    private Task<IActionResult> Done(Func<Task> action) => Guard(async () =>
    {
        await action();
        return RedirectToPage(new { id = Id });
    }, () =>
    {
        var errors = ModelState[""]?.Errors.Select(e => e.ErrorMessage).ToList();
        if (errors is { Count: > 0 }) Flash(string.Join(" ", errors), "danger");
        return RedirectToPage(new { id = Id });
    });

    private async Task LoadAsync(TakeoffChoicesInput? choices)
    {
        Sheet = await orders.GetAsync(Id, choices);
        Choices = Sheet.Takeoff.Choices;
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
    }
}
