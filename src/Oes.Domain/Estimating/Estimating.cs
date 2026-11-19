using Oes.Domain.Common;
using Oes.Domain.Crm;

namespace Oes.Domain.Estimating;

// ---------------- Catalog and price lists (what an item is vs what it costs) ----------------

public class CatalogCategory : TenantEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? TradeTypeId { get; set; }
}

/// <summary>What an item is (code, description, unit). Prices live on price lists.</summary>
public class CatalogItem : TenantEntity
{
    public Guid CategoryId { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "EA";
    public string? CoverageNotes { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class PriceListStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Archived = "archived";
    public static readonly string[] All = [Draft, Active, Archived];
}

/// <summary>A dated price list (e.g. "NE Lincoln Oct 2026"). A tenant has many; each price-book estimate picks one.</summary>
public class PriceList : TenantEntity
{
    public string Name { get; set; } = "";
    public string? RegionCode { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string Source { get; set; } = "manual";
    public string Status { get; set; } = PriceListStatuses.Draft;
    public string? Notes { get; set; }
}

public static class Activities
{
    public const string Replace = "replace";
    public const string Remove = "remove";
    public const string RemoveReplace = "r_and_r";
    public const string Install = "install";
    public const string Detach = "detach_reset";
    public const string None = "";

    public static readonly string[] All = [Replace, Remove, RemoveReplace, Install, Detach, None];

    public static string Label(string a) => a switch
    {
        Replace => "Replace", Remove => "Remove", RemoveReplace => "R&R", Install => "Install", Detach => "D&R", _ => "",
    };
}

public class PriceListItem : TenantEntity
{
    public Guid PriceListId { get; set; }
    public Guid CatalogItemId { get; set; }
    public string Activity { get; set; } = Activities.Replace;
    public decimal UnitPrice { get; set; }
    public decimal? MaterialPrice { get; set; }
    public decimal? LaborPrice { get; set; }
}

/// <summary>Products for the line-item method and order sheets.</summary>
public class Product : TenantEntity
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "EA";
    public Guid? TradeTypeId { get; set; }
    public decimal? DefaultCost { get; set; }
    public decimal DefaultPrice { get; set; }
    public bool IsMaterial { get; set; } = true;
    public string? Supplier { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>The takeoff slot this product can fill (shingle, ridge_cap…); the rep picks one product per slot.</summary>
    public string? TakeoffSlot { get; set; }
    /// <summary>Coverage for this product: units per unit of the rule's driver, replacing the rule's multiplier (e.g. a 4-bundle shingle).</summary>
    public decimal? TakeoffMultiplier { get; set; }
}

// ---------------- Takeoff rules ----------------

public static class TakeoffRounding
{
    public const string None = "none";
    public const string Up = "up";
    public const string Down = "down";
    public const string Nearest = "nearest";
    public static readonly string[] All = [Up, Nearest, Down, None];
}

public static class TakeoffOps
{
    public static readonly (string Key, string Label)[] All =
        [("gte", "≥"), ("gt", ">"), ("eq", "="), ("neq", "≠"), ("lte", "≤"), ("lt", "<")];
}

/// <summary>
/// Turns measurements into a quantity of one product, with no stored formulas:
/// qty = driver × multiplier × (1 + waste%), then rounding, minimum and maximum. Up to two
/// conditions (ANDed) and an optional on/off option gate it. The product is fixed, or picked per
/// job for the rule's slot. Ported from OES3's structured rules; supplier-agnostic.
/// </summary>
public class TakeoffRule : TenantEntity
{
    public Guid TradeTypeId { get; set; }
    public string Name { get; set; } = "";
    public Guid? ProductId { get; set; }
    public string? Slot { get; set; }
    /// <summary>A measurement field key, a derived driver (e.g. total_area_squares, ridge_hip), or "flat".</summary>
    public string Driver { get; set; } = "flat";
    public decimal Multiplier { get; set; } = 1m;
    public decimal WastePercent { get; set; }
    /// <summary>Use the measurement's suggested waste when it has one.</summary>
    public bool UseSuggestedWaste { get; set; }
    public string RoundMode { get; set; } = TakeoffRounding.Up;
    public decimal RoundTo { get; set; } = 1m;
    public decimal MinQty { get; set; }
    public decimal? MaxQty { get; set; }
    public string? Cond1Field { get; set; }
    public string? Cond1Op { get; set; }
    public decimal? Cond1Value { get; set; }
    public string? Cond2Field { get; set; }
    public string? Cond2Op { get; set; }
    public decimal? Cond2Value { get; set; }
    public string? OptionKey { get; set; }
    public string? OptionLabel { get; set; }
    public bool OptionDefaultOn { get; set; } = true;
    /// <summary>Line description instead of the product name.</summary>
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public int Sort { get; set; }
}

// ---------------- Estimates ----------------

public static class EstimateMethods
{
    public const string PriceBook = "price_book";
    public const string LineItem = "line_item";
}

public static class EstimateStatuses
{
    public const string Draft = "draft";
    public const string Issued = "issued";
    public const string Signed = "signed";
    public const string Declined = "declined";
    public const string Shelved = "shelved";
}

public static class TaxBases
{
    public const string Materials = "materials";
    public const string All = "all";
    public const string None = "none";
}

public class Estimate : TenantEntity, IArchivable
{
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid OpportunityId { get; set; }
    public Guid? PropertyId { get; set; }
    public Guid? ClaimId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string Method { get; set; } = EstimateMethods.PriceBook;
    public Guid? PriceListId { get; set; }
    public string Status { get; set; } = EstimateStatuses.Draft;
    public DateOnly? ExpiresOn { get; set; }

    public decimal OverheadPct { get; set; }
    public decimal ProfitPct { get; set; }
    public decimal TaxRate { get; set; }
    public string TaxBasis { get; set; } = TaxBases.Materials;
    public bool ShowLinePrices { get; set; } = true;
    public string? Terms { get; set; }
    public string? Notes { get; set; }

    // Cached from lines; recomputed on every change.
    public decimal Subtotal { get; set; }
    public decimal Overhead { get; set; }
    public decimal Profit { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    public int LatestVersionNo { get; set; }
    public Guid? SignedVersionId { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

/// <summary>A block of an estimate: usually one structure + trade (e.g. "Main House – Roofing").</summary>
public class EstimateSection : TenantEntity
{
    public Guid EstimateId { get; set; }
    public string Name { get; set; } = "";
    public Guid? StructureId { get; set; }
    public Guid? TradeTypeId { get; set; }
    public Guid? MeasurementSetId { get; set; }
    public string? ScopeOfWork { get; set; }
    /// <summary>The last takeoff run on this section (slot picks and options), to pre-fill the next one.</summary>
    public string? TakeoffJson { get; set; }
    public int Sort { get; set; }
}

public static class LineKinds
{
    public const string PriceBook = "price_book";
    public const string Product = "product";
    public const string Misc = "misc";
}

public static class ApprovalStatuses
{
    public const string None = "none";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>
/// An estimate line. Prices are copied when the line is added: later price list or product
/// changes never alter it. Exceptions (misc lines, prices below default) need approval.
/// </summary>
public class EstimateLine : TenantEntity
{
    public Guid EstimateId { get; set; }
    public Guid SectionId { get; set; }
    public string Kind { get; set; } = LineKinds.PriceBook;
    public Guid? CatalogItemId { get; set; }
    public Guid? PriceListItemId { get; set; }
    public Guid? ProductId { get; set; }
    /// <summary>Set when the takeoff generated this line; a re-run replaces only these.</summary>
    public Guid? TakeoffRuleId { get; set; }
    public string Activity { get; set; } = "";
    public string? Code { get; set; }
    public string Description { get; set; } = "";
    public decimal Qty { get; set; }
    public string Unit { get; set; } = "EA";
    public decimal UnitPrice { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? DefaultUnitPrice { get; set; }
    public bool IsMaterial { get; set; }
    public bool IsTaxable { get; set; } = true;
    public string? Notes { get; set; }
    public int Sort { get; set; }

    public string ApprovalStatus { get; set; } = ApprovalStatuses.None;
    public string? ApprovalReason { get; set; }
    public Guid? ApprovalRequestedBy { get; set; }
    public Guid? ApprovalDecidedBy { get; set; }
    public DateTime? ApprovalDecidedAt { get; set; }
    public string? ApprovalNote { get; set; }

    public decimal Amount => Math.Round(Qty * UnitPrice, 2, MidpointRounding.AwayFromZero);
}

public static class VersionStatuses
{
    public const string Issued = "issued";
    public const string Signed = "signed";
    public const string Declined = "declined";
    public const string Superseded = "superseded";
}

/// <summary>
/// A frozen copy of an estimate, made on issue. ContentJson holds every section, line, price,
/// term and measurement used, so it can be reprinted or audited after anything else changes.
/// </summary>
public class EstimateVersion : TenantEntity
{
    public Guid EstimateId { get; set; }
    public int VersionNo { get; set; }
    public string Status { get; set; } = VersionStatuses.Issued;
    public Guid? IssuedByUserId { get; set; }
    public string? RevisionReason { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Overhead { get; set; }
    public decimal Profit { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string ContentJson { get; set; } = "{}";
    public string ContentHash { get; set; } = "";
    public DateTime? SignedAt { get; set; }
    public string? SignerName { get; set; }
}

public static class ScopeImportStatuses
{
    public const string Review = "review";
    public const string Imported = "imported";
    public const string Discarded = "discarded";
}

/// <summary>A carrier scope read by AI into proposed estimate lines, waiting for a person to review and add them.</summary>
public class ScopeImport : TenantEntity
{
    public Guid EstimateId { get; set; }
    public Guid DocumentId { get; set; }
    public string FileName { get; set; } = "";
    public string Status { get; set; } = ScopeImportStatuses.Review;
    public string? Carrier { get; set; }
    /// <summary>The carrier's price list as printed on the scope (e.g. NENE8X_NOV26).</summary>
    public string? CarrierPriceList { get; set; }
    public string? Notes { get; set; }
    /// <summary>The AI's raw answer, kept for audit.</summary>
    public string ExtractedJson { get; set; } = "{}";
    /// <summary>The proposed lines with their price-list matches (JSON array).</summary>
    public string LinesJson { get; set; } = "[]";
    public Guid? ImportedByUserId { get; set; }
    public DateTime? ImportedAt { get; set; }
}
