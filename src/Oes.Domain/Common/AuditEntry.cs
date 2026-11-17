namespace Oes.Domain.Common;

/// <summary>Append-only record of a security-relevant or data-changing action.</summary>
public class AuditEntry : TenantEntity
{
    public Guid? UserId { get; set; }
    public required string Action { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? Summary { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>
/// Per-tenant counter behind human-readable numbers such as P-2026-0311.
/// One row per (tenant, key, year); the year is 0 for sequences that never reset.
/// </summary>
public class TenantSequence : ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Key { get; set; }
    public int Year { get; set; }
    public long LastValue { get; set; }
}
