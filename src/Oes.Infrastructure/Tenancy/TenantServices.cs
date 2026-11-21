using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Tenancy;

public static class OesClaims
{
    public const string TenantId = "oes:tenant";
}

/// <summary>
/// The signed-in user. Permissions are loaded from the database once per request (union of
/// the user's roles in the current tenant), so role edits take effect on the next request.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor http, OesDbContext db, ITenantContext tenant) : ICurrentUser
{
    private IReadOnlySet<string>? _permissions;

    public Guid? UserId =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsAuthenticated => http.HttpContext?.User.Identity?.IsAuthenticated == true;

    public async Task<bool> HasPermissionAsync(string permissionKey, CancellationToken ct = default) =>
        (await GetPermissionsAsync(ct)).Contains(permissionKey);

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken ct = default)
    {
        if (_permissions is not null) return _permissions;
        if (UserId is not { } userId || tenant.TenantId is null) return _permissions = new HashSet<string>();

        var keys = await (
            from m in db.Memberships
            where m.UserId == userId && m.Status == MembershipStatus.Active
            from mr in db.MembershipRoles.Where(r => r.MembershipId == m.Id)
            from rp in db.RolePermissions.Where(p => p.RoleId == mr.RoleId)
            select rp.PermissionKey).Distinct().ToListAsync(ct);

        return _permissions = keys.ToHashSet(StringComparer.Ordinal);
    }
}

public sealed record TenantChoice(Guid TenantId, string Name, string Slug);

public sealed class MembershipLookup(OesDbContext db)
{
    /// <summary>Active tenants this user may sign in to. Crosses tenants on purpose (login only).</summary>
    public Task<List<TenantChoice>> ActiveTenantsForUserAsync(Guid userId, CancellationToken ct = default) =>
        (from m in db.Memberships.IgnoreQueryFilters([OesDbContext.TenantFilter])
         join t in db.Tenants on m.TenantId equals t.Id
         where m.UserId == userId && m.Status == MembershipStatus.Active && t.Status == TenantStatus.Active
         orderby t.Name
         select new TenantChoice(t.Id, t.Name, t.Slug)).ToListAsync(ct);

    public async Task<bool> IsActiveMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default) =>
        await (from m in db.Memberships.IgnoreQueryFilters([OesDbContext.TenantFilter])
               join t in db.Tenants on m.TenantId equals t.Id
               where m.UserId == userId && m.TenantId == tenantId
                     && m.Status == MembershipStatus.Active && t.Status == TenantStatus.Active
               select m.Id).AnyAsync(ct);
}

public sealed record ProvisionTenantRequest(string Name, string Slug, string TimeZone, AppUser Owner);

/// <summary>
/// Creates a tenant with the default roles and makes the owner an Administrator.
/// Runs inside a platform scope because the tenant doesn't exist yet when it starts.
/// </summary>
public sealed class TenantProvisioner(OesDbContext db, TenantContext tenantContext)
{
    public async Task<Tenant> ProvisionAsync(ProvisionTenantRequest req, CancellationToken ct = default)
    {
        using var _ = tenantContext.BeginPlatformScope();

        var tenant = new Tenant { Name = req.Name, Slug = req.Slug.ToLowerInvariant(), TimeZone = req.TimeZone };
        db.Tenants.Add(tenant);

        Role? admin = null;
        foreach (var template in DefaultRoles.All)
        {
            var role = new Role
            {
                TenantId = tenant.Id,
                Name = template.Name,
                Description = template.Description,
                IsSystem = true,
                Permissions = template.PermissionKeys.Distinct()
                    .Select(k => new RolePermission { TenantId = tenant.Id, PermissionKey = k }).ToList(),
            };
            db.Roles.Add(role);
            admin ??= role;
        }

        var membership = new TenantMembership { TenantId = tenant.Id, UserId = req.Owner.Id, Status = MembershipStatus.Active };
        membership.Roles.Add(new MembershipRole { TenantId = tenant.Id, RoleId = admin!.Id });
        db.Memberships.Add(membership);

        await Crm.TenantDefaults.EnsureAsync(db, tenant.Id, ct);

        db.AuditEntries.Add(new Domain.Common.AuditEntry
        {
            TenantId = tenant.Id,
            UserId = req.Owner.Id,
            Action = "tenant.provisioned",
            EntityType = nameof(Tenant),
            EntityId = tenant.Id,
            Summary = $"Tenant {tenant.Name} created",
        });

        await db.SaveChangesAsync(ct);
        return tenant;
    }
}
