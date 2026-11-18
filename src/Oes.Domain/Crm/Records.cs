using Oes.Domain.Common;

namespace Oes.Domain.Crm;

/// <summary>Record kinds that notes, documents and activity can be linked to.</summary>
public static class EntityTypes
{
    public const string Property = "property";
    public const string Contact = "contact";
    public const string Opportunity = "opportunity";
    public const string Estimate = "estimate";
    public const string Project = "project";
    public const string Trade = "trade";
    public const string Claim = "claim";
    public const string WorkOrder = "work_order";
}

public static class ItemTypes
{
    public const string Note = "note";
    public const string Document = "document";
    public const string Activity = "activity";
    public const string Task = "task";
    public const string Appointment = "appointment";
    public const string Email = "email";
    public const string Sms = "sms";
}

public class Note : TenantEntity
{
    public string Body { get; set; } = "";
    public Guid? AuthorUserId { get; set; }
    public bool IsPinned { get; set; }
    public DateTime? EditedAt { get; set; }

    /// <summary>The record the note was written on (also present in record_links as IsPrimary).</summary>
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
}

public class Document : TenantEntity
{
    public string StorageKey { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? CategoryKey { get; set; }
    public string Source { get; set; } = "upload";
    public Guid? UploadedByUserId { get; set; }

    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public class ActivityEvent : TenantEntity
{
    public string EventType { get; set; } = "";
    public Guid? ActorUserId { get; set; }
    public string Summary { get; set; } = "";
    public string? DataJson { get; set; }

    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
}

/// <summary>
/// One link table for rollups. An item written on a record is linked to that record and to
/// every record above it, so "everything under this property" is an index lookup.
/// </summary>
public class RecordLink : TenantEntity
{
    public string ItemType { get; set; } = "";
    public Guid ItemId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public bool IsPrimary { get; set; }
}
