using Oes.Domain.Common;
using Oes.Domain.Crm;

namespace Oes.Domain.Production;

public static class WorkflowLevels
{
    public const string Project = "project";
    public const string Trade = "trade";
}

/// <summary>
/// What a stage means. Automation and gates key off roles, never stage names or IDs
/// (OES3 had a dead "Work Completed" trigger and hard-coded stage IDs 7 and 8).
/// </summary>
public static class StageRoles
{
    public const string Initial = "initial";
    public const string ProductionStart = "production_start";
    public const string MaterialsOrdered = "materials_ordered";
    public const string MaterialsDelivered = "materials_delivered";
    public const string Labor = "labor";
    public const string FinalWalkthrough = "final_walkthrough";
    public const string Complete = "complete";
    public const string Invoiced = "invoiced";
    public const string Paid = "paid";
    public const string Closed = "closed";
    public const string Custom = "";

    public static readonly string[] Project = [Initial, ProductionStart, Complete, Invoiced, Paid, Closed, Custom];
    public static readonly string[] Trade = [Initial, MaterialsOrdered, MaterialsDelivered, Labor, FinalWalkthrough, Complete, Custom];

    public static string Label(string role) => role switch
    {
        Initial => "Start", ProductionStart => "Production started", MaterialsOrdered => "Materials ordered",
        MaterialsDelivered => "Materials delivered", Labor => "Work in progress", FinalWalkthrough => "Final walkthrough",
        Complete => "Complete", Invoiced => "Invoiced", Paid => "Paid", Closed => "Closed", _ => "No special role",
    };
}

public class WorkflowStage : TenantEntity
{
    public string Level { get; set; } = WorkflowLevels.Project;
    public string Name { get; set; } = "";
    public string Role { get; set; } = StageRoles.Custom;
    public string Color { get; set; } = "#64748b";
    public int Sort { get; set; }
    public int? StaleAfterDays { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Project : TenantEntity, IArchivable
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid OpportunityId { get; set; }
    public Guid? PropertyId { get; set; }
    public Guid? EstimateId { get; set; }
    public Guid? EstimateVersionId { get; set; }
    public Guid? ClaimId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid StageId { get; set; }
    public DateTime? StageChangedAt { get; set; }

    /// <summary>Signed version total; change orders are added on top (see ContractValue).</summary>
    public decimal OriginalContract { get; set; }
    public decimal ApprovedChangeOrders { get; set; }
    public decimal ContractValue => OriginalContract + ApprovedChangeOrders;

    public DateTime? CompletedAt { get; set; }
    public DateTime? RemovedFromBoardAt { get; set; }
    public string? RemovalReasonKey { get; set; }
    public string? RemovalNote { get; set; }
    public string? Notes { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public class ProjectTrade : TenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid TradeTypeId { get; set; }
    public string? Description { get; set; }
    public Guid StageId { get; set; }
    public DateTime? StageChangedAt { get; set; }
    public decimal Amount { get; set; }
    public DateOnly? MaterialDate { get; set; }
    public DateOnly? WorkDate { get; set; }
    public string? CrewNote { get; set; }
    public int Sort { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RemovedFromBoardAt { get; set; }
    public string? RemovalReasonKey { get; set; }
    public string? RemovalNote { get; set; }
}

/// <summary>Stage history for projects and trades (who moved what, when, and what automation did).</summary>
public class WorkflowStageChange : TenantEntity
{
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public Guid? FromStageId { get; set; }
    public Guid ToStageId { get; set; }
    public Guid? ByUserId { get; set; }
    public bool IsAutomatic { get; set; }
    public string? Note { get; set; }
}

public static class ChangeOrderStatuses
{
    public const string Draft = "draft";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public static readonly string[] All = [Draft, Approved, Rejected];
}

/// <summary>A change to the signed contract. Approved change orders add to the project's contract value.</summary>
public class ChangeOrder : TenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid? ProjectTradeId { get; set; }
    public int Sequence { get; set; }
    public string Description { get; set; } = "";
    public string? Reason { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = ChangeOrderStatuses.Draft;
    public bool IsInsuranceSupplement { get; set; }
    /// <summary>The amount is the sum of <see cref="ChangeOrderLine"/>s rather than typed in.</summary>
    public bool IsItemized { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
}

public static class ChangeOrderLineKinds
{
    public const string PriceBook = "price_book";
    public const string Product = "product";
    public const string Custom = "custom";
}

/// <summary>One item on an itemized change order. A negative quantity is a credit. Prices are copied when added.</summary>
public class ChangeOrderLine : TenantEntity
{
    public Guid ChangeOrderId { get; set; }
    public int Sort { get; set; }
    public string Kind { get; set; } = ChangeOrderLineKinds.Custom;
    public Guid? PriceListItemId { get; set; }
    public Guid? ProductId { get; set; }
    public string? Code { get; set; }
    public string Description { get; set; } = "";
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "EA";
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public static class RemovalReasons
{
    public const string Type = "board_removal_reason";
}
