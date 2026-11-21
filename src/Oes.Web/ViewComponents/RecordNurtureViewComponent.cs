using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;

namespace Oes.Web.ViewComponents;

public sealed class NurtureCardModel
{
    public required Guid ContactId { get; init; }
    public Guid? OpportunityId { get; init; }
    public required List<EnrollmentRow> Enrollments { get; init; }
    public required List<Sequence> Available { get; init; }
    public required Dictionary<Guid, DateTime> NextLocal { get; init; }
}

/// <summary>Nurture enrollments on a contact or opportunity page, with an enroll form.</summary>
public sealed class RecordNurtureViewComponent(NurtureService nurture, OesDbContext db, TenantTime time, ICurrentUser user) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string entityType, Guid entityId)
    {
        if (!await user.HasPermissionAsync(Permissions.NurtureEnroll)) return Content("");
        Guid contactId;
        Guid? oppId = null;
        if (entityType == EntityTypes.Opportunity)
        {
            oppId = entityId;
            contactId = await db.Opportunities.Where(o => o.Id == entityId).Select(o => o.PrimaryContactId).FirstAsync();
        }
        else contactId = entityId;

        var rows = await nurture.EnrollmentsAsync(oppId is null ? contactId : null, oppId, null, 20);
        var next = new Dictionary<Guid, DateTime>();
        foreach (var r in rows.Where(r => r.NextRunAt is not null)) next[r.Id] = await time.ToLocalAsync(r.NextRunAt!.Value);
        var available = await nurture.EnrollableAsync();
        if (rows.Count == 0 && available.Count == 0) return Content("");
        return View("/Pages/Shared/_RecordNurture.cshtml", new NurtureCardModel
        {
            ContactId = contactId, OpportunityId = oppId, Enrollments = rows, NextLocal = next,
            Available = available.Where(s => rows.All(r => r.SequenceId != s.Id || r.Status != EnrollmentStatuses.Active)).ToList(),
        });
    }
}
