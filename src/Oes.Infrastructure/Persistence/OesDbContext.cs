using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Identity;

namespace Oes.Infrastructure.Persistence;

public class OesDbContext(DbContextOptions<OesDbContext> options, ITenantContext tenant)
    : IdentityUserContext<AppUser, Guid>(options)
{
    /// <summary>Name of the global tenant filter, for the rare audited IgnoreQueryFilters([TenantFilter]) call.</summary>
    public const string TenantFilter = "Tenant";

    public ITenantContext Tenant { get; } = tenant;

    /// <summary>Read by the tenant query filter on every query. Guid.Empty matches nothing.</summary>
    public Guid CurrentTenantId => Tenant.TenantId ?? Guid.Empty;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<Guid>().HaveConversion<GuidBinaryConverter>().HaveColumnType("binary(16)");
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("datetime(6)");
        b.Properties<string>().HaveMaxLength(255);
        b.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Identity (users are platform-level, not tenant-owned)
        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.LastName).HasMaxLength(100);
            e.Property(u => u.PasswordHash).HasMaxLength(500);
            e.Ignore(u => u.FullName);
            e.Ignore(u => u.Initials);
        });
        b.Entity<IdentityUserClaim<Guid>>(e =>
        {
            e.ToTable("user_claims");
            e.Property(c => c.ClaimValue).HasMaxLength(1000);
        });
        b.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        b.Entity<IdentityUserToken<Guid>>(e =>
        {
            e.ToTable("user_tokens");
            e.Property(t => t.Value).HasMaxLength(2000);
        });

        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.Slug).HasMaxLength(60);
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.TimeZone).HasMaxLength(64);
            e.Property(t => t.PostalAddress).HasMaxLength(500);
        });

        b.Entity<TenantMembership>(e =>
        {
            e.ToTable("tenant_memberships");
            e.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
            e.HasIndex(m => m.UserId);
            e.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(m => m.Roles).WithOne().HasForeignKey(r => r.MembershipId).OnDelete(DeleteBehavior.Cascade);
            e.Property(m => m.Title).HasMaxLength(100);
        });

        b.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.Property(r => r.Name).HasMaxLength(100);
            e.Property(r => r.Description).HasMaxLength(500);
            e.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions");
            e.Property(p => p.PermissionKey).HasMaxLength(100);
            e.HasIndex(p => new { p.RoleId, p.PermissionKey }).IsUnique();
            e.HasIndex(p => p.TenantId);
        });

        b.Entity<MembershipRole>(e =>
        {
            e.ToTable("membership_roles");
            e.HasIndex(r => new { r.MembershipId, r.RoleId }).IsUnique();
            e.HasIndex(r => r.TenantId);
            e.HasOne(r => r.Role).WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entries");
            e.Property(a => a.Action).HasMaxLength(100);
            e.Property(a => a.EntityType).HasMaxLength(100);
            e.Property(a => a.Summary).HasMaxLength(1000);
            e.Property(a => a.DetailsJson).HasColumnType("json").Metadata.SetMaxLength(null);
            e.Property(a => a.IpAddress).HasMaxLength(45);
            e.HasIndex(a => new { a.TenantId, a.CreatedAt });
            e.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        });

        b.Entity<TenantSequence>(e =>
        {
            e.ToTable("tenant_sequences");
            e.HasKey(s => new { s.TenantId, s.Key, s.Year });
            e.Property(s => s.Key).HasMaxLength(50);
        });

        ApplyTenantFilters(b);
    }

    /// <summary>
    /// Adds the named tenant filter to every ITenantOwned entity. New entities get it
    /// automatically; the isolation tests fail if any tenant-owned type is missing it.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder b)
    {
        var method = typeof(OesDbContext).GetMethod(nameof(ApplyTenantFilter),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        foreach (var entityType in b.Model.GetEntityTypes().ToList())
        {
            if (entityType.BaseType is null && typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                method.MakeGenericMethod(entityType.ClrType).Invoke(this, [b]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder b) where T : class, ITenantOwned =>
        b.Entity<T>().HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
}

/// <summary>All times are stored in UTC; this marks values read back as UTC.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
