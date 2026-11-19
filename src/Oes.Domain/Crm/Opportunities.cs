using Oes.Domain.Common;

namespace Oes.Domain.Crm;

public static class Outcomes
{
    public const string Open = "open";
    public const string Won = "won";
    public const string Lost = "lost";
}

/// <summary>Per-tenant sales pipeline stage. Outcome marks the won/lost end stages.</summary>
public class PipelineStage : TenantEntity
{
    public string Name { get; set; } = "";
    public int Sort { get; set; }
    public string Color { get; set; } = "#3b82f6";
    public string Outcome { get; set; } = Outcomes.Open;
    public bool IsDefault { get; set; }
    public int? StaleAfterHours { get; set; }
    /// <summary>Open opportunities in this stage show up in the prospecting call list.</summary>
    public bool InProspecting { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// The sales pipeline record (a lead is an opportunity). Needs a contact; the property is
/// attached when the address is collected.
/// </summary>
public class Opportunity : TenantEntity, IArchivable
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid PrimaryContactId { get; set; }
    public Guid? PropertyId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? SourceKey { get; set; }
    public Guid StageId { get; set; }
    public string Outcome { get; set; } = Outcomes.Open;
    public string? LostReasonKey { get; set; }
    public decimal? ExpectedValue { get; set; }

    public bool IsInsurance { get; set; }
    public string? PerilKey { get; set; }
    public DateOnly? DateOfLoss { get; set; }
    public Guid? StormEventId { get; set; }

    public int ContactAttempts { get; set; }
    public DateTime? LastContactAt { get; set; }
    public Guid? LastDispositionId { get; set; }
    /// <summary>When the lead comes back to the call list; null = due now.</summary>
    public DateTime? RecycleAt { get; set; }
    /// <summary>Who is working it in the call list (expires after a few minutes).</summary>
    public Guid? LockedByUserId { get; set; }
    public DateTime? LockedAt { get; set; }
    public DateTime? StageChangedAt { get; set; }
    public DateTime? WonAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Description { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public class OpportunityStageChange : TenantEntity
{
    public Guid OpportunityId { get; set; }
    public Guid? FromStageId { get; set; }
    public Guid ToStageId { get; set; }
    public Guid? ByUserId { get; set; }
    public string? Note { get; set; }
}

/// <summary>Simple per-tenant pick lists (lead source, roles, perils, lost reasons…).</summary>
public class LookupValue : TenantEntity
{
    public string Type { get; set; } = "";
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Sort { get; set; }
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
}

public static class LookupTypes
{
    public const string LeadSource = "lead_source";
    public const string PropertyContactRole = "contact_role_property";
    public const string LostReason = "lost_reason";
    public const string Peril = "peril";
    public const string DocumentCategory = "document_category";
}

/// <summary>
/// A call outcome ("No answer", "Appointment set"…). Decides when the lead comes back to the
/// call list, whether it counts as a contact attempt, and optionally moves the stage.
/// </summary>
public class Disposition : TenantEntity
{
    public string Name { get; set; } = "";
    public int Sort { get; set; }
    public string Color { get; set; } = "#64748b";
    public int? RecycleDays { get; set; }
    public Guid? MovesToStageId { get; set; }
    /// <summary>Used when the stage it moves to is a lost stage.</summary>
    public string? LostReasonKey { get; set; }
    public bool CountsAsAttempt { get; set; } = true;
    /// <summary>Takes the lead off the call list for good and opts the contact out of marketing.</summary>
    public bool IsDoNotContact { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Each disposition set on an opportunity (call history).</summary>
public class OpportunityDisposition : TenantEntity
{
    public Guid OpportunityId { get; set; }
    public Guid DispositionId { get; set; }
    public Guid? ByUserId { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// A named set of filters on a list page. Personal (UserId set) or shared with the whole
/// company (UserId null). A personal view can be the user's default for that list.
/// </summary>
public class SavedView : TenantEntity
{
    public Guid? UserId { get; set; }
    public string ListKey { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The list's filters as a JSON object of query-string values (only known keys).</summary>
    public string CriteriaJson { get; set; } = "{}";
    public bool IsDefault { get; set; }
}
