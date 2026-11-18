using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Subcontractors;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.WorkOrders;

public class ViewModel(WorkOrderService workOrders, RecordItems items, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public WorkOrderDetail Detail { get; private set; } = null!;
    public TimelineModel Timeline { get; private set; } = null!;

    /// <summary>The raw link is shown once, right after it's created (only its hash is stored).</summary>
    public string? NewLink => TempData["PortalLink"] as string;

    public Task<IActionResult> OnGetAsync(string? tab) => Guard(async () =>
    {
        Detail = await workOrders.GetAsync(Id);
        Timeline = await TimelineModel.LoadAsync(items, lookups, EntityTypes.WorkOrder, Id, tab, null);
        return Page();
    });

    private Task<IActionResult> Do(Func<Task> action, string? message = null) => Guard(async () =>
    {
        await action();
        if (message is not null) Flash(message);
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostDetailsAsync(DateOnly? workDate, string? arrivalTime, string? instructions) =>
        Do(() => workOrders.UpdateAsync(Id, workDate, arrivalTime, instructions), "Saved.");

    public Task<IActionResult> OnPostLineAsync(Guid? rateId, string? description, decimal qty, string? unit, decimal? rate) =>
        Do(() => workOrders.AddLineAsync(Id, rateId, description, qty, unit, rate));

    public Task<IActionResult> OnPostRemoveLineAsync(Guid lineId) => Do(() => workOrders.RemoveLineAsync(lineId));

    public Task<IActionResult> OnPostIssueAsync() => Guard(async () =>
    {
        var r = await workOrders.IssueAsync(Id);
        TempData["PortalLink"] = r.Link;
        Flash(r.Emailed ? $"Issued and emailed to {r.EmailedTo}." : "Issued. There's no email on file for this sub, so copy the link below and send it yourself.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostNewLinkAsync() => Guard(async () =>
    {
        var r = await workOrders.NewLinkAsync(Id);
        TempData["PortalLink"] = r.Link;
        Flash(r.Emailed ? $"New link emailed to {r.EmailedTo}. The old link no longer works." : "New link created. The old link no longer works.");
        return RedirectToPage(new { id = Id });
    });

    public Task<IActionResult> OnPostReviseAsync() => Do(() => workOrders.ReviseAsync(Id), "Back to draft. The portal link was turned off until you issue it again.");

    public Task<IActionResult> OnPostStatusAsync(string status) => Do(() => workOrders.SetStatusAsync(Id, status), "Status updated.");
}
