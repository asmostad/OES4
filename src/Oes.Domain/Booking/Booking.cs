using Oes.Domain.Common;

namespace Oes.Domain.Booking;

/// <summary>A kind of appointment people can book online, e.g. "Free roof inspection".</summary>
public class BookingType : TenantEntity
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 60;
    /// <summary>Starts are offered every N minutes inside the hosts' hours.</summary>
    public int SlotIntervalMinutes { get; set; } = 30;
    public int BufferBeforeMinutes { get; set; }
    public int BufferAfterMinutes { get; set; } = 30;
    /// <summary>How soon the earliest slot can be (e.g. 1440 = not before tomorrow this time).</summary>
    public int MinNoticeMinutes { get; set; } = 720;
    public int DaysAhead { get; set; } = 21;
    /// <summary>Most bookings of this type per host per day; null = no limit.</summary>
    public int? DailyCapPerHost { get; set; }
    /// <summary>Appointment type (lookup key) used on the calendar.</summary>
    public string AppointmentTypeKey { get; set; } = "inspection";
    public bool AsksAddress { get; set; } = true;
    public bool CreatesOpportunity { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public List<BookingTypeHost> Hosts { get; set; } = [];
}

public class BookingTypeHost : TenantEntity
{
    public Guid BookingTypeId { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>A user's weekly hours for online booking, in the company's time zone.</summary>
public class AvailabilityRule : TenantEntity
{
    public Guid UserId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}

public static class BookingStatuses
{
    public const string Booked = "booked";
    public const string Cancelled = "cancelled";
}

public static class BookingReview
{
    /// <summary>Waiting for staff to create or link the contact.</summary>
    public const string New = "new";
    public const string Accepted = "accepted";
    /// <summary>Spam or a mistake: the appointment is cancelled and nothing goes into the CRM.</summary>
    public const string Dismissed = "dismissed";
}

/// <summary>
/// An online booking. What the visitor typed stays here; staff review it and then create a new
/// contact or link an existing one. Anonymous input never writes onto existing records directly.
/// </summary>
public class Booking : TenantEntity
{
    public Guid BookingTypeId { get; set; }
    public Guid HostUserId { get; set; }
    public Guid AppointmentId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Status { get; set; } = BookingStatuses.Booked;
    public string ReviewStatus { get; set; } = BookingReview.New;

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? Line1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Notes { get; set; }
    /// <summary>The visitor ticked "you may text me about this appointment".</summary>
    public bool TextConsent { get; set; }

    public Guid? ContactId { get; set; }
    public Guid? OpportunityId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    /// <summary>When the day-before reminder email went out.</summary>
    public DateTime? ReminderSentAt { get; set; }
    /// <summary>How many times the customer moved it from their link (capped).</summary>
    public int RescheduleCount { get; set; }
    public string? SourceIp { get; set; }

    /// <summary>"{host}|{start ticks}" while booked, null once cancelled. Unique: one booking per host per start.</summary>
    public string? SlotKey { get; set; }
}
