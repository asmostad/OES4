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
