using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.ViewComponents;

/// <summary>Tasks and appointments card for any record page: <c>@await Component.InvokeAsync("RecordAgenda", new { entityType, entityId })</c>.</summary>
public sealed class RecordAgendaViewComponent(WorkService work, CrmLookups lookups, TenantTime time, ICurrentUser user) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string entityType, Guid entityId) =>
        View("/Pages/Shared/_RecordAgenda.cshtml", await AgendaModel.LoadAsync(work, lookups, time, entityType, entityId, user.UserId));
}
