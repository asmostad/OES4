using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Subcontractors;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.WorkOrders;

/// <summary>
/// The work order as a printable document for the crew. Issuing happens here too (?handler=Issue): the order is
/// marked issued, its PDF rendered and saved, and the sub emailed the portal link with the PDF attached.
/// </summary>
[HasPermission(Permissions.WorkOrdersManage)]
public class PrintModel(WorkOrderService workOrders, OesDbContext db, ITenantContext tenant) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public WorkOrderDetail Detail { get; private set; } = null!;
    public Tenant Company { get; private set; } = null!;

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        await LoadAsync();
        return Page();
    });

    public Task<IActionResult> OnGetPdfAsync() => Guard(async () =>
    {
        await LoadAsync();
        return await PdfAsync(Detail.WorkOrder.Number, Footer, $"/WorkOrders/Print/{Id}");
    });

    /// <summary>Issues the work order and sends it to the sub with its PDF; back to the work order page with the outcome.</summary>
    public Task<IActionResult> OnPostIssueAsync() => Guard(async () =>
    {
        var back = $"/WorkOrders/View/{Id}";
        await workOrders.MarkIssuedAsync(Id);
        await LoadAsync();
        ViewData[PdfDocument.ViewDataKey] = true;
        string Outcome(IssueResult r)
        {
            TempData["PortalLink"] = r.Link;
            return r.Emailed ? $"Issued and emailed to {r.EmailedTo}." : "Issued. There's no email on file for this sub, so copy the link below and send it yourself.";
        }
        return new PdfPageResult(Detail.WorkOrder.Number, Footer, back)
        {
            Deliver = async pdf => Outcome(await workOrders.SendIssuedAsync(Id, pdf)) + " The PDF is saved on the work order.",
            WithoutPdf = async () => Outcome(await workOrders.SendIssuedAsync(Id, null)),
        };
    }, () =>
    {
        var errors = ModelState[""]?.Errors.Select(e => e.ErrorMessage).ToList();
        if (errors is { Count: > 0 }) Flash(string.Join(" ", errors), "danger");
        return Redirect($"/WorkOrders/View/{Id}");
    });

    private string Footer => $"{Company.Name} · work order {Detail.WorkOrder.Number}";

    private async Task LoadAsync()
    {
        Detail = await workOrders.GetAsync(Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
    }
}
