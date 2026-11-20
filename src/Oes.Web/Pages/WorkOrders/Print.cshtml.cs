using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Subcontractors;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.WorkOrders;

/// <summary>The work order as a printable document for the crew (print or save as PDF from the browser).</summary>
[HasPermission(Permissions.WorkOrdersManage)]
public class PrintModel(WorkOrderService workOrders, OesDbContext db, ITenantContext tenant) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public WorkOrderDetail Detail { get; private set; } = null!;
    public Tenant Company { get; private set; } = null!;

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        Detail = await workOrders.GetAsync(Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        return Page();
    });

    public Task<IActionResult> OnGetPdfAsync() => Guard(async () =>
    {
        Detail = await workOrders.GetAsync(Id);
        Company = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        return await PdfAsync(Detail.WorkOrder.Number, $"{Company.Name} · work order {Detail.WorkOrder.Number}", $"/WorkOrders/Print/{Id}");
    });
}
