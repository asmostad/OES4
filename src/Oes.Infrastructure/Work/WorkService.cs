using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Domain.Tenancy;
using Oes.Domain.Work;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Work;

public sealed class TaskInput
{
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public DateOnly? DueDate { get; set; }
    public TimeOnly? DueTime { get; set; }
    public string Priority { get; set; } = TaskPriorities.Normal;
    public Guid? AssignedToUserId { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
}

public sealed class AppointmentInput
{
    public string Title { get; set; } = "";
    public string TypeKey { get; set; } = "other";
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; } = new(9, 0);
    public int DurationMinutes { get; set; } = 60;
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
}

public sealed record TaskRow(Guid Id, string Title, string? Notes, DateTime? DueLocal, bool HasDueTime, string Priority, Guid AssignedToUserId,
    string AssigneeName, DateTime? CompletedAt, string? EntityType, Guid? EntityId, string? RecordUrl, bool IsOverdue, bool IsDueToday, bool CanEdit);

public sealed record AppointmentRow(Guid Id, string Title, string TypeKey, DateTime StartLocal, DateTime EndLocal, string? Location, string? Notes,
    Guid AssignedToUserId, string AssigneeName, string Status, string? EntityType, Guid? EntityId, string? RecordUrl, bool CanEdit);

/// <summary>Tasks and appointments: on records (rolled up via record_links) or standalone.</summary>
public sealed class WorkService(
    OesDbContext db, RecordScope scope, RecordItems items, ICurrentUser user, IClock clock, TenantTime time, NotificationService notifications)
{
    // ---------------- Shared ----------------

    private async Task<Guid> AssigneeAsync(Guid? requested, CancellationToken ct)
    {
        var id = requested ?? scope.Me ?? throw new RecordForbiddenException("Sign in first.");
        if (!await db.Memberships.AnyAsync(m => m.UserId == id && m.Status == MembershipStatus.Active, ct))
            throw new CrmRuleException("Assign it to an active user in this company.");
        return id;
    }

    private async Task<(string? Type, Guid? Id)> RecordAsync(string? entityType, Guid? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityType) || entityId is null) return (null, null);
        await scope.EnsureCanSeeAsync(entityType, entityId.Value);
        return (entityType, entityId);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, ct);
    }

    // ---------------- Tasks ----------------

    public async Task<Guid> CreateTaskAsync(TaskInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.TasksCreate);
        if (string.IsNullOrWhiteSpace(input.Title)) throw new CrmRuleException("What needs doing?");
        var (type, id) = await RecordAsync(input.EntityType, input.EntityId);
        // Anyone who can create tasks can hand one to a teammate (as in OES3); the assignee is notified.
        var assignee = await AssigneeAsync(input.AssignedToUserId, ct);

        var task = new TaskItem
        {
            Title = input.Title.Trim(), Notes = ContactService.Clean(input.Notes), Priority = input.Priority == TaskPriorities.High ? TaskPriorities.High : TaskPriorities.Normal,
            AssignedToUserId = assignee, CreatedByUserId = scope.Me, EntityType = type, EntityId = id,
        };
        await SetDueAsync(task, input.DueDate, input.DueTime);
        db.Tasks.Add(task);
        if (type is not null) await items.LinkItemAsync(ItemTypes.Task, task.Id, type, id!.Value, ct);
        var me = await NamesAsync([scope.Me ?? Guid.Empty], ct);
        notifications.Notify(assignee, "task.assigned", $"New task: {task.Title}",
            $"From {me.GetValueOrDefault(scope.Me ?? Guid.Empty, "a teammate")}{(input.DueDate is null ? "" : $", due {input.DueDate:MMM d}")}", RecordUrls.For(type, id) ?? "/Tasks");
        await db.SaveChangesAsync(ct);
        return task.Id;
    }

    private async Task SetDueAsync(TaskItem task, DateOnly? date, TimeOnly? timeOfDay)
    {
        task.HasDueTime = date is not null && timeOfDay is not null;
        // Date-only tasks sit at noon local so they stay on the right day in every view.
        task.DueAt = date is { } d ? await time.ToUtcAsync(d, timeOfDay ?? new TimeOnly(12, 0)) : null;
    }

    private async Task<TaskItem> EditableTaskAsync(Guid id, CancellationToken ct)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new RecordNotFoundException("Task");
        if (task.AssignedToUserId != scope.Me && task.CreatedByUserId != scope.Me && !await user.HasPermissionAsync(Permissions.TasksManage, ct))
            throw new RecordNotFoundException("Task");
        return task;
    }

    public async Task SetDoneAsync(Guid id, bool done, CancellationToken ct = default)
    {
        var task = await EditableTaskAsync(id, ct);
        task.CompletedAt = done ? task.CompletedAt ?? clock.UtcNow : null;
        task.CompletedByUserId = done ? scope.Me : null;
        if (done && task.EntityType is not null)
            await items.LogAsync(task.EntityType, task.EntityId!.Value, "task.completed", $"Task done: {task.Title}", ct);
        if (done && task.CreatedByUserId is { } creator)
            notifications.Notify(creator, "task.completed", $"Done: {task.Title}", null, RecordUrls.For(task.EntityType, task.EntityId) ?? "/Tasks");
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateTaskAsync(Guid id, TaskInput input, CancellationToken ct = default)
    {
        var task = await EditableTaskAsync(id, ct);
        if (string.IsNullOrWhiteSpace(input.Title)) throw new CrmRuleException("What needs doing?");
        task.Title = input.Title.Trim();
        task.Notes = ContactService.Clean(input.Notes);
        task.Priority = input.Priority == TaskPriorities.High ? TaskPriorities.High : TaskPriorities.Normal;
        await SetDueAsync(task, input.DueDate, input.DueTime);
        if (input.AssignedToUserId is { } a && a != task.AssignedToUserId)
        {
            task.AssignedToUserId = await AssigneeAsync(a, ct);
            notifications.Notify(a, "task.assigned", $"Task reassigned to you: {task.Title}", null, RecordUrls.For(task.EntityType, task.EntityId) ?? "/Tasks");
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Task list. Without tasks.view_all, a user sees tasks assigned to or created by them.
    /// When a record is given, includes everything under it (via record_links).
    /// </summary>
    public async Task<List<TaskRow>> TasksAsync(bool onlyMine, bool includeDone, string? entityType = null, Guid? entityId = null, int take = 300, CancellationToken ct = default)
    {
        var me = scope.Me;
        var q = db.Tasks.AsQueryable();
        if (entityType is not null && entityId is { } eid)
        {
            await scope.EnsureCanSeeAsync(entityType, eid);
            var ids = db.RecordLinks.Where(l => l.ItemType == ItemTypes.Task && l.EntityType == entityType && l.EntityId == eid).Select(l => l.ItemId);
            q = q.Where(t => ids.Contains(t.Id));
        }
        else if (onlyMine || !await user.HasPermissionAsync(Permissions.TasksViewAll, ct))
        {
            q = onlyMine ? q.Where(t => t.AssignedToUserId == me) : q.Where(t => t.AssignedToUserId == me || t.CreatedByUserId == me);
        }
        if (!includeDone) q = q.Where(t => t.CompletedAt == null);
        var tasks = await q.OrderBy(t => t.CompletedAt != null).ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.CreatedAt)
            .Take(take).ToListAsync(ct);
        return await RowsAsync(tasks, ct);
    }

    private async Task<List<TaskRow>> RowsAsync(List<TaskItem> tasks, CancellationToken ct)
    {
        var names = await NamesAsync(tasks.Select(t => t.AssignedToUserId), ct);
        var today = await time.TodayAsync();
        var now = clock.UtcNow;
        var manage = await user.HasPermissionAsync(Permissions.TasksManage, ct);
        var rows = new List<TaskRow>();
        foreach (var t in tasks)
        {
            DateTime? local = t.DueAt is { } due ? await time.ToLocalAsync(due) : null;
            var overdue = t.CompletedAt is null && local is { } l && (t.HasDueTime ? t.DueAt < now : DateOnly.FromDateTime(l) < today);
            var dueToday = local is { } l2 && DateOnly.FromDateTime(l2) == today;
            rows.Add(new TaskRow(t.Id, t.Title, t.Notes, local, t.HasDueTime, t.Priority, t.AssignedToUserId, names.GetValueOrDefault(t.AssignedToUserId, "?"),
                t.CompletedAt, t.EntityType, t.EntityId, RecordUrls.For(t.EntityType, t.EntityId), overdue, dueToday,
                manage || t.AssignedToUserId == scope.Me || t.CreatedByUserId == scope.Me));
        }
        return rows;
    }

    // ---------------- Appointments ----------------

    public async Task<Guid> CreateAppointmentAsync(AppointmentInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.TasksCreate);
        if (string.IsNullOrWhiteSpace(input.Title)) throw new CrmRuleException("Give the appointment a title.");
        if (input.DurationMinutes is < 5 or > 24 * 60) throw new CrmRuleException("Duration must be between 5 minutes and 24 hours.");
        var (type, id) = await RecordAsync(input.EntityType, input.EntityId);
        var assignee = await AssigneeAsync(input.AssignedToUserId, ct);
        if (assignee != scope.Me && !await user.HasPermissionAsync(Permissions.CalendarManage, ct))
            throw new RecordForbiddenException("You can only put appointments on your own calendar.");

        var starts = await time.ToUtcAsync(input.Date, input.StartTime);
        var appt = new Appointment
        {
            Title = input.Title.Trim(), TypeKey = string.IsNullOrWhiteSpace(input.TypeKey) ? "other" : input.TypeKey, StartsAt = starts,
            EndsAt = starts.AddMinutes(input.DurationMinutes), Location = ContactService.Clean(input.Location) ?? await DefaultLocationAsync(type, id, ct),
            Notes = ContactService.Clean(input.Notes), AssignedToUserId = assignee, CreatedByUserId = scope.Me, EntityType = type, EntityId = id,
        };
        db.Appointments.Add(appt);
        if (type is not null)
        {
            await items.LinkItemAsync(ItemTypes.Appointment, appt.Id, type, id!.Value, ct);
            await items.LogAsync(type, id.Value, "appointment.scheduled", $"Appointment: {appt.Title} on {input.Date:ddd MMM d} at {input.StartTime:h:mm tt}", ct);
        }
        notifications.Notify(assignee, "appointment.assigned", $"New appointment: {appt.Title}", $"{input.Date:ddd MMM d} at {input.StartTime:h:mm tt}",
            RecordUrls.For(type, id) ?? $"/Calendar?date={input.Date:yyyy-MM-dd}");
        await db.SaveChangesAsync(ct);
        return appt.Id;
    }

    /// <summary>Appointments on a property-backed record default to that address.</summary>
    private async Task<string?> DefaultLocationAsync(string? type, Guid? id, CancellationToken ct)
    {
        Guid? propertyId = type switch
        {
            EntityTypes.Property => id,
            EntityTypes.Opportunity => await db.Opportunities.Where(o => o.Id == id).Select(o => o.PropertyId).FirstOrDefaultAsync(ct),
            EntityTypes.Claim => await db.Claims.Where(c => c.Id == id).Select(c => c.PropertyId).FirstOrDefaultAsync(ct),
            EntityTypes.Project => await db.Projects.Where(p => p.Id == id).Select(p => p.PropertyId).FirstOrDefaultAsync(ct),
            EntityTypes.Trade => await (from t in db.ProjectTrades where t.Id == id join p in db.Projects on t.ProjectId equals p.Id select p.PropertyId).FirstOrDefaultAsync(ct),
            _ => null,
        };
        if (propertyId is null) return null;
        var p2 = await db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId, ct);
        return p2?.SingleLine;
    }

    public async Task SetAppointmentStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        if (status is not (AppointmentStatuses.Scheduled or AppointmentStatuses.Completed or AppointmentStatuses.Cancelled)) throw new CrmRuleException("Unknown status.");
        var a = await db.Appointments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Appointment");
        if (a.AssignedToUserId != scope.Me && a.CreatedByUserId != scope.Me && !await user.HasPermissionAsync(Permissions.CalendarManage, ct))
            throw new RecordNotFoundException("Appointment");
        a.Status = status;
        if (status == AppointmentStatuses.Cancelled) notifications.Notify(a.AssignedToUserId, "appointment.cancelled", $"Cancelled: {a.Title}", null, "/Calendar");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Edits an appointment. The assignee is told when it's moved or handed to them. If it came from an
    /// online booking, the booking moves with it (and its reminder is re-armed).
    /// </summary>
    public async Task UpdateAppointmentAsync(Guid id, AppointmentInput input, CancellationToken ct = default)
    {
        var a = await db.Appointments.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Appointment");
        var manage = await user.HasPermissionAsync(Permissions.CalendarManage, ct);
        if (a.AssignedToUserId != scope.Me && a.CreatedByUserId != scope.Me && !manage) throw new RecordNotFoundException("Appointment");
        if (a.Status == AppointmentStatuses.Cancelled) throw new CrmRuleException("This appointment was cancelled.");
        if (string.IsNullOrWhiteSpace(input.Title)) throw new CrmRuleException("Give the appointment a title.");
        if (input.DurationMinutes is < 5 or > 24 * 60) throw new CrmRuleException("Duration must be between 5 minutes and 24 hours.");
        var assignee = await AssigneeAsync(input.AssignedToUserId ?? a.AssignedToUserId, ct);
        if (assignee != a.AssignedToUserId && assignee != scope.Me && !manage)
            throw new RecordForbiddenException("You can only put appointments on your own calendar.");

        var starts = await time.ToUtcAsync(input.Date, input.StartTime);
        var moved = starts != a.StartsAt || starts.AddMinutes(input.DurationMinutes) != a.EndsAt;
        var reassigned = assignee != a.AssignedToUserId;
        a.Title = input.Title.Trim();
        a.TypeKey = string.IsNullOrWhiteSpace(input.TypeKey) ? a.TypeKey : input.TypeKey;
        a.StartsAt = starts;
        a.EndsAt = starts.AddMinutes(input.DurationMinutes);
        a.Location = ContactService.Clean(input.Location);
        a.Notes = ContactService.Clean(input.Notes);
        a.AssignedToUserId = assignee;

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.AppointmentId == a.Id && b.Status == Domain.Booking.BookingStatuses.Booked, ct);
        if (booking is not null && (moved || reassigned))
        {
            booking.StartsAt = a.StartsAt; booking.EndsAt = a.EndsAt; booking.HostUserId = assignee;
            booking.SlotKey = Scheduling.BookingService.SlotKey(assignee, a.StartsAt);
            booking.ReminderSentAt = null;
        }
        var when = $"{input.Date:ddd MMM d} at {input.StartTime:h:mm tt}";
        var url = RecordUrls.For(a.EntityType, a.EntityId) ?? $"/Calendar?date={input.Date:yyyy-MM-dd}";
        if (reassigned) notifications.Notify(assignee, "appointment.assigned", $"New appointment: {a.Title}", when, url);
        else if (moved) notifications.Notify(assignee, "appointment.moved", $"Moved: {a.Title}", $"Now {when}", url);
        if (moved && a.EntityType is not null) await items.LogAsync(a.EntityType, a.EntityId!.Value, "appointment.moved", $"Appointment moved: {a.Title}, now {when}", ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (booking is not null)
        {
            throw new CrmRuleException("They already have an online booking at that time.");
        }
    }

    /// <summary>Appointments between two local dates. Without calendar.view_all only your own (assigned or created).</summary>
    public async Task<List<AppointmentRow>> AppointmentsAsync(DateOnly from, DateOnly to, Guid? userId, string? entityType = null, Guid? entityId = null,
        bool includeCancelled = false, CancellationToken ct = default)
    {
        var me = scope.Me;
        var q = db.Appointments.AsQueryable();
        if (entityType is not null && entityId is { } eid)
        {
            await scope.EnsureCanSeeAsync(entityType, eid);
            var ids = db.RecordLinks.Where(l => l.ItemType == ItemTypes.Appointment && l.EntityType == entityType && l.EntityId == eid).Select(l => l.ItemId);
            q = q.Where(a => ids.Contains(a.Id));
        }
        else
        {
            var (start, end) = await time.DayRangeUtcAsync(from, to);
            q = q.Where(a => a.StartsAt < end && a.EndsAt > start);
            if (!await user.HasPermissionAsync(Permissions.CalendarViewAll, ct)) q = q.Where(a => a.AssignedToUserId == me || a.CreatedByUserId == me);
            else if (userId is { } u) q = q.Where(a => a.AssignedToUserId == u);
        }
        if (!includeCancelled) q = q.Where(a => a.Status != AppointmentStatuses.Cancelled);
        var list = await q.OrderBy(a => a.StartsAt).Take(500).ToListAsync(ct);
        var names = await NamesAsync(list.Select(a => a.AssignedToUserId), ct);
        var manage = await user.HasPermissionAsync(Permissions.CalendarManage, ct);
        var rows = new List<AppointmentRow>();
        foreach (var a in list)
            rows.Add(new AppointmentRow(a.Id, a.Title, a.TypeKey, await time.ToLocalAsync(a.StartsAt), await time.ToLocalAsync(a.EndsAt), a.Location, a.Notes,
                a.AssignedToUserId, names.GetValueOrDefault(a.AssignedToUserId, "?"), a.Status, a.EntityType, a.EntityId, RecordUrls.For(a.EntityType, a.EntityId),
                manage || a.AssignedToUserId == me || a.CreatedByUserId == me));
        return rows;
    }

    public sealed record MyDay(int Overdue, int DueToday, List<AppointmentRow> Appointments);

    public async Task<MyDay> MyDayAsync(CancellationToken ct = default)
    {
        var tasks = await TasksAsync(onlyMine: true, includeDone: false, ct: ct);
        var today = await time.TodayAsync();
        var appts = (await AppointmentsAsync(today, today, scope.Me, ct: ct)).Where(a => a.AssignedToUserId == scope.Me).ToList();
        return new MyDay(tasks.Count(t => t.IsOverdue), tasks.Count(t => t.IsDueToday && !t.IsOverdue), appts);
    }
}
