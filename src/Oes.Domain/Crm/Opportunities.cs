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
