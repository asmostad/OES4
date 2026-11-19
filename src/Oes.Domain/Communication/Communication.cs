using Oes.Domain.Common;

namespace Oes.Domain.Communication;

public static class Channels
{
    public const string Email = "email";
    public const string Sms = "sms";
    public static readonly string[] All = [Email, Sms];
}

public static class MessagePurposes
{
    public const string Transactional = "transactional";
    public const string Marketing = "marketing";
    public static readonly string[] All = [Transactional, Marketing];
}

public static class ConsentStatuses
{
    public const string OptedIn = "opted_in";
    public const string OptedOut = "opted_out";
    public const string Unknown = "unknown";
}

public static class Directions
{
    public const string Outbound = "outbound";
    public const string Inbound = "inbound";
}

public static class MessageStatuses
{
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Received = "received";
}

public static class SuppressionReasons
{
    public const string Stop = "stop";
    public const string Unsubscribe = "unsubscribe";
    public const string Bounce = "bounce";
    public const string Complaint = "complaint";
    public const string Manual = "manual";
}

/// <summary>Email or SMS template. Bodies are plain text with {{merge.fields}}; email is HTML-encoded on send.</summary>
public class MessageTemplate : TenantEntity
{
    public string Channel { get; set; } = Channels.Email;
    public string Name { get; set; } = "";
    public string? Subject { get; set; }
    public string Body { get; set; } = "";
    public string Category { get; set; } = MessagePurposes.Transactional;
    public bool IsActive { get; set; } = true;
}

/// <summary>One row per message, linked to every record it belongs to through record_links.</summary>
public class EmailMessage : TenantEntity
{
    public string Direction { get; set; } = Directions.Outbound;
    public string FromAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime SentAt { get; set; }
    public Guid? SentByUserId { get; set; }
    public string Status { get; set; } = MessageStatuses.Sent;
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid? BlastId { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public List<EmailRecipient> Recipients { get; set; } = [];
}

public class EmailRecipient : TenantEntity
{
    public Guid EmailMessageId { get; set; }
    public string Kind { get; set; } = "to";
    public string Address { get; set; } = "";
    public Guid? ContactId { get; set; }
}

public class SmsMessage : TenantEntity
{
    public string Direction { get; set; } = Directions.Outbound;
    public string FromNumber { get; set; } = "";
    public string ToNumber { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime At { get; set; }
    public string Status { get; set; } = MessageStatuses.Sent;
    public string? ProviderSid { get; set; }
    public string? Error { get; set; }
    public Guid? SentByUserId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? TemplateId { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    /// <summary>An inbound text from an unknown number that someone dismissed from the unmatched inbox.</summary>
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>Consent history: the latest row per (contact, channel, purpose) is the current status.</summary>
public class ContactConsent : TenantEntity
{
    public Guid ContactId { get; set; }
    public string Channel { get; set; } = Channels.Sms;
    public string Purpose { get; set; } = MessagePurposes.Transactional;
    public string Status { get; set; } = ConsentStatuses.OptedIn;
    /// <summary>verbal, written, web_form, keyword (START/STOP), import…</summary>
    public string Source { get; set; } = "";
    public string? Evidence { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public DateTime At { get; set; }
}

/// <summary>An address (normalized email or E.164 phone) that must not be messaged. Checked on every send.</summary>
public class Suppression : TenantEntity
{
    public string Channel { get; set; } = Channels.Sms;
    public string Address { get; set; } = "";
    /// <summary>"all" blocks everything; "marketing" blocks marketing only.</summary>
    public string Scope { get; set; } = "all";
    public string Reason { get; set; } = SuppressionReasons.Manual;
    public DateTime? RemovedAt { get; set; }
}

/// <summary>The tenant's texting number. Inbound SMS is routed to the tenant by the number it was sent to.</summary>
public class TenantPhoneNumber : TenantEntity
{
    public string Number { get; set; } = "";
    public string Provider { get; set; } = "dev";
    public bool IsActive { get; set; } = true;
}

public static class BlastStatuses
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Cancelled = "cancelled";
}

public static class BlastRecipientStatuses
{
    public const string Pending = "pending";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

/// <summary>Who a blast goes to. Stored as JSON on the blast; resolved into recipients when it's scheduled.</summary>
public sealed class BlastAudience
{
    /// <summary>Contact statuses to include (lead/customer/past_customer). Empty = all.</summary>
    public List<string> Statuses { get; set; } = [];
    /// <summary>Only contacts on an opportunity tied to this storm.</summary>
    public Guid? StormEventId { get; set; }
    /// <summary>Only contacts from this lead source.</summary>
    public string? SourceKey { get; set; }
    public bool IncludeOrganizations { get; set; }
}

/// <summary>A marketing email to many contacts, sent by the worker in throttled batches.</summary>
public class Blast : TenantEntity
{
    public string Name { get; set; } = "";
    public Guid? TemplateId { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string AudienceJson { get; set; } = "{}";
    /// <summary>The mailbox it's sent from (the user who created it, until shared mailboxes exist).</summary>
    public Guid SenderUserId { get; set; }
    public int ThrottlePerMinute { get; set; } = 30;
    public string Status { get; set; } = BlastStatuses.Draft;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int Total { get; set; }
    public int SentCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

public class BlastRecipient : TenantEntity
{
    public Guid BlastId { get; set; }
    public Guid ContactId { get; set; }
    public string Address { get; set; } = "";
    public string Status { get; set; } = BlastRecipientStatuses.Pending;
    /// <summary>Set when the sender picks the row up; with the status change it stops two runs sending the same email.</summary>
    public DateTime? ClaimedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? Error { get; set; }
    public Guid? EmailMessageId { get; set; }
}
