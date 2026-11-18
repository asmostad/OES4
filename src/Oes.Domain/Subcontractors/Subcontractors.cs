using Oes.Domain.Common;

namespace Oes.Domain.Subcontractors;

public static class SubStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string DoNotUse = "do_not_use";
    public static readonly string[] All = [Active, Inactive, DoNotUse];
}

/// <summary>
/// A subcontractor profile on an organization contact (decided 2026-11-16): name, phone, email
/// and crew live on the contact; this holds what's specific to subbing.
/// </summary>
public class Subcontractor : TenantEntity
{
    public Guid ContactId { get; set; }

    /// <summary>Comma-separated trade codes the sub does (roofing, gutters…).</summary>
    public string? TradeCodes { get; set; }
    public string Status { get; set; } = SubStatuses.Active;
    public string? Notes { get; set; }

    /// <summary>Earliest expiry among current insurance documents (cached for lists and alerts).</summary>
    public DateOnly? InsuranceExpiresOn { get; set; }

    public IEnumerable<string> Trades => (TradeCodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public static class SubDocTypes
{
    public const string GeneralLiability = "general_liability";
    public const string WorkersComp = "workers_comp";
    public const string W9 = "w9";
    public const string License = "license";
    public const string Agreement = "agreement";
    public static readonly string[] All = [GeneralLiability, WorkersComp, W9, License, Agreement];
    public static readonly string[] Insurance = [GeneralLiability, WorkersComp];

    public static string Label(string key) => key switch
    {
        GeneralLiability => "General liability", WorkersComp => "Workers' comp", W9 => "W-9", License => "License", Agreement => "Sub agreement", _ => key,
    };
}

public class SubcontractorDocument : TenantEntity
{
    public Guid SubcontractorId { get; set; }
    public string DocTypeKey { get; set; } = SubDocTypes.GeneralLiability;
    public Guid DocumentId { get; set; }
    public DateOnly? ExpiresOn { get; set; }
}

/// <summary>A sub's price for a unit of work. Effective-dated and never edited in place: a new rate ends the old one.</summary>
public class SubcontractorRate : TenantEntity
{
    public Guid SubcontractorId { get; set; }
    public Guid? TradeTypeId { get; set; }
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "SQ";
    public decimal Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public static class WorkOrderStatuses
{
    public const string Draft = "draft";
    public const string Issued = "issued";
    public const string Completed = "completed";
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";
    public static readonly string[] All = [Draft, Issued, Completed, Paid, Cancelled];
}

public static class AcceptStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
}

public class WorkOrder : TenantEntity
{
    public string Number { get; set; } = "";
    public Guid ProjectId { get; set; }
    public Guid ProjectTradeId { get; set; }
    public Guid SubcontractorId { get; set; }
    public string Status { get; set; } = WorkOrderStatuses.Draft;
    public string AcceptStatus { get; set; } = AcceptStatuses.Pending;
    public string? AcceptedName { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? DeclineReason { get; set; }
    public decimal Amount { get; set; }
    public DateOnly? WorkDate { get; set; }
    public string? ArrivalTime { get; set; }
    public string? Instructions { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public class WorkOrderLine : TenantEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid? RateId { get; set; }
    public string Description { get; set; } = "";
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "EA";
    public decimal Rate { get; set; }
    public int Sort { get; set; }

    public decimal Amount => Math.Round(Qty * Rate, 2, MidpointRounding.AwayFromZero);
}
