using Oes.Domain.Common;

namespace Oes.Domain.Tenancy;

public enum TenantStatus { Active = 1, Suspended = 2, Closed = 3 }

/// <summary>A customer company using OES4. Platform-level, so not tenant-owned.</summary>
public class Tenant : Entity
{
    public required string Name { get; set; }

    /// <summary>URL-safe short name used by public pages, e.g. /book/{slug}.</summary>
    public required string Slug { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    /// <summary>IANA time zone, e.g. "America/Chicago". Dates are stored in UTC and shown in this zone.</summary>
    public string TimeZone { get; set; } = "America/Chicago";

    /// <summary>Postal address printed in marketing email footers (CAN-SPAM).</summary>
    public string? PostalAddress { get; set; }

    /// <summary>File-store key of the logo shown in the sidebar, or null for the default mark.</summary>
    public string? LogoFileKey { get; set; }

    // Estimate defaults (Administration › Settings); copied onto each new estimate, which can change them.
    public string? EstimateTerms { get; set; }
    /// <summary>Terms for insurance work; falls back to <see cref="EstimateTerms"/>.</summary>
    public string? InsuranceEstimateTerms { get; set; }
    public int EstimateValidDays { get; set; } = 30;
    /// <summary>Sales tax percent for new estimates.</summary>
    public decimal DefaultTaxRate { get; set; }
    public decimal InsuranceOverheadPct { get; set; } = 10m;
    public decimal InsuranceProfitPct { get; set; } = 10m;
}

public enum MembershipStatus { Invited = 1, Active = 2, Disabled = 3 }

/// <summary>Links a user to a tenant. One user can belong to several tenants.</summary>
public class TenantMembership : TenantEntity
{
    public Guid UserId { get; set; }
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public string? Title { get; set; }
    public DateTime? LastSignInAt { get; set; }

    public List<MembershipRole> Roles { get; set; } = [];
}

/// <summary>A per-tenant role. Roles carry permissions; code never checks role names.</summary>
public class Role : TenantEntity
{
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Seeded roles can be renamed and edited but not deleted.</summary>
    public bool IsSystem { get; set; }

    public List<RolePermission> Permissions { get; set; } = [];
}

public class RolePermission : TenantEntity
{
    public Guid RoleId { get; set; }
    public required string PermissionKey { get; set; }
}

public class MembershipRole : TenantEntity
{
    public Guid MembershipId { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}
