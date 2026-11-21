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

    /// <summary>When the push job handled it (sent to the user's phones, or found none). Null = not yet.</summary>
    public DateTime? PushedAt { get; set; }
}

/// <summary>The notification kinds that also buzz the user's phone. The rest stay in the bell only.</summary>
public static class PushKinds
{
    public static readonly string[] All =
    [
        "task.assigned", "task.due",
        "appointment.assigned", "appointment.invited", "appointment.moved", "appointment.cancelled",
        "booking.new", "booking.moved", "booking.cancelled",
        "sms.received", "sms.unmatched",
    ];
}

/// <summary>A phone's Expo push address, registered by the field app after the user allows notifications.</summary>
public class DevicePushToken : TenantEntity
{
    public Guid UserId { get; set; }
    /// <summary>"ExponentPushToken[…]".</summary>
    public string Token { get; set; } = "";
    /// <summary>ios / android.</summary>
    public string Platform { get; set; } = "";
    public DateTime LastSeenAt { get; set; }
    /// <summary>Set when Expo says the phone can't receive pushes any more (app removed, permission revoked).</summary>
    public DateTime? DisabledAt { get; set; }
}
