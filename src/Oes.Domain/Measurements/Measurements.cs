using Oes.Domain.Common;

namespace Oes.Domain.Measurements;

/// <summary>How a trade is measured. Behavior keys off this and the trade Code, never the name.</summary>
public static class MeasurementModes
{
    public const string Roof = "roof";           // whole-roof areas and linear edges
    public const string Elevation = "elevation"; // per wall side (front, rear, left, right)
    public const string Linear = "linear";       // runs and counts (gutters, fencing)
    public const string None = "none";

    public static readonly string[] All = [Roof, Elevation, Linear, None];

    /// <summary>The surfaces a mode is measured on. Empty string = the whole structure.</summary>
    public static string[] Surfaces(string mode) => mode == Elevation ? ["front", "rear", "left", "right"] : [""];
}

/// <summary>Per-tenant trade (Roofing, Siding, Gutters…). Shared by measurements, estimates and projects.</summary>
public class TradeType : TenantEntity
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string MeasurementMode { get; set; } = MeasurementModes.None;
    public string Color { get; set; } = "#64748b";
    public string Icon { get; set; } = "bi-hammer";
    public int Sort { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A measurement field for a trade (e.g. Roofing › Valleys, LF). Tenants can add their own.</summary>
public class MeasurementFieldDef : TenantEntity
{
    public Guid TradeTypeId { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? Group { get; set; }
    public int Sort { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class MeasurementSources
{
    public static readonly string[] All = ["manual", "eagleview", "hover", "gaf_quickmeasure", "roofr", "xactimate", "field_tape", "other"];
}

/// <summary>
/// One version of a structure's measurements for a trade. Never overwritten: a new report or
/// correction creates a new version and the previous one stops being current.
/// </summary>
public class MeasurementSet : TenantEntity
{
    public Guid PropertyId { get; set; }
    public Guid StructureId { get; set; }
    public Guid TradeTypeId { get; set; }
    public int VersionNo { get; set; }
    public bool IsCurrent { get; set; }
    public string Source { get; set; } = "manual";
    public Guid? SourceDocumentId { get; set; }
    public DateOnly? MeasuredOn { get; set; }
    public Guid? MeasuredByUserId { get; set; }
    public Guid? SupersedesId { get; set; }
    /// <summary>When an AI read the report: its raw answer, kept for audit.</summary>
    public string? ExtractionJson { get; set; }
    public string? Notes { get; set; }

    public List<MeasurementValue> Values { get; set; } = [];
}

public class MeasurementValue : TenantEntity
{
    public Guid SetId { get; set; }

    /// <summary>"" for whole-structure values; front/rear/left/right for elevations.</summary>
    public string SurfaceKey { get; set; } = "";
    public string FieldKey { get; set; } = "";
    public decimal Value { get; set; }
}
