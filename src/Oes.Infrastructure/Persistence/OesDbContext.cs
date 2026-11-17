using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Domain.Crm;
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

    // CRM core
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactMethod> ContactMethods => Set<ContactMethod>();
    public DbSet<ContactRelationship> ContactRelationships => Set<ContactRelationship>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<PropertyContact> PropertyContacts => Set<PropertyContact>();
    public DbSet<PropertyStructure> PropertyStructures => Set<PropertyStructure>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<OpportunityStageChange> OpportunityStageChanges => Set<OpportunityStageChange>();
    public DbSet<LookupValue> LookupValues => Set<LookupValue>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<RecordLink> RecordLinks => Set<RecordLink>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<Guid>().HaveConversion<GuidBinaryConverter>().HaveColumnType("binary(16)");
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("datetime(6)");
        // Oracle's MySQL driver returns DATE columns as DateTime and can't read them into DateOnly directly.
        b.Properties<DateOnly>().HaveConversion<DateOnlyConverter>().HaveColumnType("date");
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

        ConfigureCrm(b);
        ApplyTenantFilters(b);
    }

    private static void ConfigureCrm(ModelBuilder b)
    {
        b.Entity<Contact>(e =>
        {
            e.ToTable("contacts");
            e.Property(c => c.Type).HasMaxLength(20);
            e.Property(c => c.FirstName).HasMaxLength(100);
            e.Property(c => c.LastName).HasMaxLength(100);
            e.Property(c => c.OrganizationName).HasMaxLength(200);
            e.Property(c => c.DisplayName).HasMaxLength(200);
            e.Property(c => c.Status).HasMaxLength(20);
            e.Property(c => c.SourceKey).HasMaxLength(60);
            e.Property(c => c.CautionNote).HasMaxLength(500);
            e.Property(c => c.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(c => new { c.TenantId, c.DisplayName });
            e.HasIndex(c => new { c.TenantId, c.LastName, c.FirstName });
            e.HasIndex(c => new { c.TenantId, c.OwnerUserId });
            e.HasMany(c => c.Methods).WithOne().HasForeignKey(m => m.ContactId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(c => c.IsOrganization);
        });

        b.Entity<ContactMethod>(e =>
        {
            e.ToTable("contact_methods");
            e.Property(m => m.Kind).HasMaxLength(20);
            e.Property(m => m.Value).HasMaxLength(254);
            e.Property(m => m.NormalizedValue).HasMaxLength(254);
            e.Property(m => m.Label).HasMaxLength(50);
            e.HasIndex(m => new { m.TenantId, m.NormalizedValue });
        });

        b.Entity<ContactRelationship>(e =>
        {
            e.ToTable("contact_relationships");
            e.Property(r => r.Title).HasMaxLength(100);
            e.HasIndex(r => new { r.TenantId, r.PersonContactId });
            e.HasIndex(r => new { r.TenantId, r.OrganizationContactId });
            e.HasOne<Contact>().WithMany().HasForeignKey(r => r.PersonContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Contact>().WithMany().HasForeignKey(r => r.OrganizationContactId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Property>(e =>
        {
            e.ToTable("properties");
            e.Property(p => p.Line1).HasMaxLength(200);
            e.Property(p => p.Line2).HasMaxLength(100);
            e.Property(p => p.City).HasMaxLength(100);
            e.Property(p => p.State).HasMaxLength(50);
            e.Property(p => p.PostalCode).HasMaxLength(20);
            e.Property(p => p.Country).HasMaxLength(2);
            e.Property(p => p.NormalizedKey).HasMaxLength(320);
            e.Property(p => p.Kind).HasMaxLength(20);
            e.Property(p => p.Latitude).HasPrecision(9, 6);
            e.Property(p => p.Longitude).HasPrecision(9, 6);
            e.Property(p => p.Notes).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(p => new { p.TenantId, p.NormalizedKey }).IsUnique();
            e.HasIndex(p => new { p.TenantId, p.PostalCode });
            e.HasMany(p => p.Structures).WithOne().HasForeignKey(s => s.PropertyId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(p => p.SingleLine);
            e.Ignore(p => p.CityLine);
        });

        b.Entity<PropertyContact>(e =>
        {
            e.ToTable("property_contacts");
            e.Property(pc => pc.RoleKey).HasMaxLength(60);
            e.HasIndex(pc => new { pc.TenantId, pc.PropertyId });
            e.HasIndex(pc => new { pc.TenantId, pc.ContactId });
            e.HasOne<Property>().WithMany().HasForeignKey(pc => pc.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Contact>().WithMany().HasForeignKey(pc => pc.ContactId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(pc => pc.IsCurrent);
        });

        b.Entity<PropertyStructure>(e =>
        {
            e.ToTable("property_structures");
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Kind).HasMaxLength(50);
        });

        b.Entity<PipelineStage>(e =>
        {
            e.ToTable("pipeline_stages");
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Color).HasMaxLength(20);
            e.Property(s => s.Outcome).HasMaxLength(20);
            e.HasIndex(s => new { s.TenantId, s.Sort });
        });

        b.Entity<Opportunity>(e =>
        {
            e.ToTable("opportunities");
            e.Property(o => o.Number).HasMaxLength(30);
            e.Property(o => o.Name).HasMaxLength(200);
            e.Property(o => o.SourceKey).HasMaxLength(60);
            e.Property(o => o.Outcome).HasMaxLength(20);
            e.Property(o => o.LostReasonKey).HasMaxLength(60);
            e.Property(o => o.PerilKey).HasMaxLength(60);
            e.Property(o => o.ExpectedValue).HasPrecision(12, 2);
            e.Property(o => o.Description).HasColumnType("text").Metadata.SetMaxLength(null);
            e.HasIndex(o => new { o.TenantId, o.Number }).IsUnique();
            e.HasIndex(o => new { o.TenantId, o.StageId });
            e.HasIndex(o => new { o.TenantId, o.OwnerUserId });
            e.HasIndex(o => new { o.TenantId, o.PropertyId });
            e.HasIndex(o => new { o.TenantId, o.PrimaryContactId });
            e.HasOne<Contact>().WithMany().HasForeignKey(o => o.PrimaryContactId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Property>().WithMany().HasForeignKey(o => o.PropertyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PipelineStage>().WithMany().HasForeignKey(o => o.StageId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OpportunityStageChange>(e =>
        {
            e.ToTable("opportunity_stage_changes");
            e.Property(c => c.Note).HasMaxLength(500);
            e.HasIndex(c => new { c.TenantId, c.OpportunityId });
        });

        b.Entity<LookupValue>(e =>
        {
            e.ToTable("lookup_values");
            e.Property(l => l.Type).HasMaxLength(60);
            e.Property(l => l.Key).HasMaxLength(60);
            e.Property(l => l.Label).HasMaxLength(100);
            e.Property(l => l.Color).HasMaxLength(20);
            e.HasIndex(l => new { l.TenantId, l.Type, l.Key }).IsUnique();
        });

        b.Entity<Note>(e =>
        {
            e.ToTable("notes");
            e.Property(n => n.Body).HasColumnType("text").Metadata.SetMaxLength(null);
            e.Property(n => n.EntityType).HasMaxLength(30);
        });

        b.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.Property(d => d.StorageKey).HasMaxLength(120);
            e.Property(d => d.FileName).HasMaxLength(255);
            e.Property(d => d.ContentType).HasMaxLength(120);
            e.Property(d => d.Sha256).HasMaxLength(64);
            e.Property(d => d.CategoryKey).HasMaxLength(60);
            e.Property(d => d.Source).HasMaxLength(20);
            e.Property(d => d.EntityType).HasMaxLength(30);
        });

        b.Entity<ActivityEvent>(e =>
        {
            e.ToTable("activity_events");
            e.Property(a => a.EventType).HasMaxLength(60);
            e.Property(a => a.Summary).HasMaxLength(500);
            e.Property(a => a.EntityType).HasMaxLength(30);
            e.Property(a => a.DataJson).HasColumnType("json").Metadata.SetMaxLength(null);
        });

        b.Entity<RecordLink>(e =>
        {
            e.ToTable("record_links");
            e.Property(l => l.ItemType).HasMaxLength(20);
            e.Property(l => l.EntityType).HasMaxLength(30);
            e.HasIndex(l => new { l.TenantId, l.EntityType, l.EntityId, l.ItemType });
            e.HasIndex(l => new { l.TenantId, l.ItemType, l.ItemId, l.EntityType, l.EntityId }).IsUnique();
        });
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

public sealed class DateOnlyConverter() : ValueConverter<DateOnly, DateTime>(
    d => d.ToDateTime(TimeOnly.MinValue),
    v => DateOnly.FromDateTime(v));

/// <summary>All times are stored in UTC; this marks values read back as UTC.</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
