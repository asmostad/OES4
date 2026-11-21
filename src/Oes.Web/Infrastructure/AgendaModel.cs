using Oes.Domain.Work;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;

namespace Oes.Web.Infrastructure;

/// <summary>Model for the _RecordAgenda partial: open tasks and upcoming appointments on a record and everything under it.</summary>
public sealed class AgendaModel
{
    public required string EntityType { get; init; }
    public required Guid EntityId { get; init; }
    public required List<TaskRow> Tasks { get; init; }
    public required List<AppointmentRow> Appointments { get; init; }
    public required List<Option> Users { get; init; }
    public required List<Option> AppointmentTypes { get; init; }
    public Guid? Me { get; init; }
    public DateOnly Today { get; init; }
    public DateTime NowLocal { get; init; }

    public static async Task<AgendaModel> LoadAsync(WorkService work, CrmLookups lookups, TenantTime time, string entityType, Guid entityId, Guid? me)
    {
        var today = await time.TodayAsync();
        var now = await time.ToLocalAsync(DateTime.UtcNow);
        return new AgendaModel
        {
            EntityType = entityType, EntityId = entityId, Me = me, Today = today, NowLocal = now,
            Tasks = await work.TasksAsync(onlyMine: false, includeDone: false, entityType, entityId),
            Appointments = (await work.AppointmentsAsync(today.AddDays(-7), today.AddDays(365), null, entityType, entityId))
                .Where(a => a.Status != AppointmentStatuses.Cancelled).ToList(),
            Users = await lookups.UsersAsync(),
            AppointmentTypes = await lookups.ValuesAsync(Oes.Domain.Work.AppointmentTypes.LookupType),
        };
    }
}
