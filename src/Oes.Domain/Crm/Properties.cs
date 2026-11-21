using Oes.Domain.Common;

namespace Oes.Domain.Crm;

public static class PropertyKinds
{
    public const string Residential = "residential";
    public const string Commercial = "commercial";
    public const string MultiFamily = "multi_family";

    public static readonly string[] All = [Residential, Commercial, MultiFamily];
}

/// <summary>
/// The root record. Contacts, opportunities, estimates and projects hang off a property.
/// NormalizedKey (unique per tenant) is how duplicates are caught at entry.
/// </summary>
public class Property : TenantEntity, IArchivable
{
    public string Line1 { get; set; } = "";
    public string? Line2 { get; set; }
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "US";

    public string NormalizedKey { get; set; } = "";
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public string Kind { get; set; } = PropertyKinds.Residential;
    public string? Notes { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime? ArchivedAt { get; set; }
    /// <summary>Set when this property was merged into another; the other one holds everything now.</summary>
    public Guid? MergedIntoId { get; set; }

    public List<PropertyStructure> Structures { get; set; } = [];

    public string SingleLine =>
        string.Join(", ", new[] { Line2 is null ? Line1 : $"{Line1} {Line2}", City, $"{State} {PostalCode}".Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    public string CityLine => string.Join(", ", new[] { City, $"{State} {PostalCode}".Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Contact ↔ property with a role and history (a sale ends the link instead of deleting it).</summary>
public class PropertyContact : TenantEntity
{
    public Guid PropertyId { get; set; }
    public Guid ContactId { get; set; }
    public string RoleKey { get; set; } = "owner";
    public bool IsPrimary { get; set; }
    public DateOnly? StartedOn { get; set; }
    public DateOnly? EndedOn { get; set; }

    public bool IsCurrent => EndedOn is null;
}

/// <summary>Main House, Detached Garage, Building B… Shared by measurements and estimate areas.</summary>
public class PropertyStructure : TenantEntity
{
    public Guid PropertyId { get; set; }
    public string Name { get; set; } = "Main House";
    public string? Kind { get; set; }
    public int Sort { get; set; }
}
