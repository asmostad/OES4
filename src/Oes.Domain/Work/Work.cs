using Oes.Domain.Common;

namespace Oes.Domain.Work;

public static class TaskPriorities
{
    public const string Normal = "normal";
    public const string High = "high";
}

/// <summary>A to-do, optionally on a record (and rolled up to the records above it).</summary>
public class TaskItem : TenantEntity
{
    public string Title { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>When it's due (UTC). Date-only tasks are stored at noon local time so they never drift across days.</summary>
    public DateTime? DueAt { get; set; }
    public bool HasDueTime { get; set; }
    public string Priority { get; set; } = TaskPriorities.Normal;
    public Guid AssignedToUserId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }

    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    public bool IsOpen => CompletedAt is null;
}

public static class AppointmentStatuses
{
    public const string Scheduled = "scheduled";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

public class Appointment : TenantEntity
{
    public string Title { get; set; } = "";
    public string TypeKey { get; set; } = "other";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    /// <summary>Whole days (local midnight to midnight, possibly several days); no times shown.</summary>
    public bool IsAllDay { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public Guid AssignedToUserId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string Status { get; set; } = AppointmentStatuses.Scheduled;

    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
}

/// <summary>Team members invited to an appointment besides the person it's assigned to. It shows on their calendar too.</summary>
public class AppointmentAttendee : TenantEntity
{
    public Guid AppointmentId { get; set; }
    public Guid UserId { get; set; }
}

public static class AppointmentTypes
{
    public const string LookupType = "appointment_type";
}

/// <summary>An in-app notification for one user (the bell). Email/push delivery comes later.</summary>
public class Notification : TenantEntity
{
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }

    /// <summary>App-relative link (always local; validated before redirecting).</summary>
    public string? Url { get; set; }
    public DateTime? ReadAt { get; set; }

    /// <summary>Set for automatic alerts so each one is sent once (e.g. "task.overdue:{id}:{due}").</summary>
    public string? DedupeKey { get; set; }
}
