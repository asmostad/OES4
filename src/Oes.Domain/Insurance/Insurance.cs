using Oes.Domain.Common;
using Oes.Domain.Crm;

namespace Oes.Domain.Insurance;

/// <summary>
/// Platform default carrier list shared by all tenants. Tenants never edit these rows;
/// they edit their own TenantCarrier copy.
/// </summary>
public class Carrier : Entity
{
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? ClaimsEmail { get; set; }
    public string? ClaimsFax { get; set; }
    public string? Website { get; set; }
    public string? PreferredSubmission { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// A tenant's copy of a carrier. CarrierId null = a carrier the tenant added themselves.
/// Null override fields fall back to the platform carrier, so platform corrections still flow through.
/// </summary>
public class TenantCarrier : TenantEntity
{
    public Guid? CarrierId { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? ClaimsEmail { get; set; }
    public string? ClaimsFax { get; set; }
    public string? Website { get; set; }
    public string? PreferredSubmission { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public bool IsHidden { get; set; }
}

public static class SubmissionMethods
{
    public static readonly string[] All = ["email", "portal", "fax", "mail", "phone", "xactanalysis"];
}

/// <summary>A storm (date, peril, size, area) that leads, claims and blasts can be grouped by.</summary>
public class StormEvent : TenantEntity, IArchivable
{
    public string Name { get; set; } = "";
    public DateOnly EventDate { get; set; }
    public string PerilKey { get; set; } = "hail";
    public decimal? MaxHailSizeIn { get; set; }
    public int? MaxWindMph { get; set; }

    /// <summary>Comma-separated ZIP codes in the affected area (map area later).</summary>
    public string? ZipCodes { get; set; }
    public string? Notes { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public IEnumerable<string> Zips =>
        (ZipCodes ?? "").Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public static class ClaimStatuses
{
    public const string NotFiled = "not_filed";
    public const string Open = "open";
    public const string Approved = "approved";
    public const string Denied = "denied";
    public const string Closed = "closed";

    public static readonly string[] All = [NotFiled, Open, Approved, Denied, Closed];
}

/// <summary>
/// First-class claim, shared by the opportunity, estimates, project and supplements.
/// Every field except the opportunity can stay empty until the homeowner files (decided 2026-11-16).
/// </summary>
public class InsuranceClaim : TenantEntity, IArchivable
{
    public Guid OpportunityId { get; set; }
    public Guid? PropertyId { get; set; }
    public Guid? TenantCarrierId { get; set; }
    public Guid? StormEventId { get; set; }
    public string? ClaimNumber { get; set; }
    public string? PolicyNumber { get; set; }
    public DateOnly? DateOfLoss { get; set; }
    public string? PerilKey { get; set; }
    public decimal? Deductible { get; set; }
    public Guid? AdjusterContactId { get; set; }
    public Guid? DeskAdjusterContactId { get; set; }
    public string Status { get; set; } = ClaimStatuses.NotFiled;
    public DateOnly? FiledOn { get; set; }
    public DateOnly? InspectionOn { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string? Notes { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

public static class SupplementStatuses
{
    public const string Needed = "needed";
    public const string Sent = "sent";
    public const string Approved = "approved";
    public const string Partial = "partial";
    public const string Denied = "denied";

    public static readonly string[] All = [Needed, Sent, Approved, Partial, Denied];
}

/// <summary>A supplement request on a claim, with its history of amounts and dates.</summary>
public class Supplement : TenantEntity
{
    public Guid ClaimId { get; set; }
    public string Status { get; set; } = SupplementStatuses.Needed;
    public string Reason { get; set; } = "";
    public decimal? RequestedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public DateOnly? SentOn { get; set; }
    public DateOnly? RespondedOn { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public string? Notes { get; set; }
}
