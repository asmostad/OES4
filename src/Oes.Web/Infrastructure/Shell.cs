using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Persistence;

namespace Oes.Web.Infrastructure;

public sealed record ShellInfo(
    string FullName, string Initials, string Email, string? RoleNames,
    string TenantName, int TenantCount, IReadOnlySet<string> Permissions)
{
    public bool Can(string? permission) => permission is null || Permissions.Contains(permission);
}

/// <summary>Loads what the layout needs (user card, tenant name, visible nav) once per request.</summary>
public sealed class Shell(OesDbContext db, ICurrentUser user, ITenantContext tenant)
{
    private ShellInfo? _info;

    public async Task<ShellInfo?> GetAsync()
    {
        if (_info is not null) return _info;
        if (user.UserId is not { } userId || tenant.TenantId is not { } tenantId) return null;

        var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId);
        var tenantName = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).FirstAsync();
        var roles = await (from m in db.Memberships
                           where m.UserId == userId
                           from mr in m.Roles
                           select mr.Role!.Name).ToListAsync();
        var tenantCount = await db.Memberships.IgnoreQueryFilters([OesDbContext.TenantFilter])
            .CountAsync(m => m.UserId == userId && m.Status == Domain.Tenancy.MembershipStatus.Active);

        return _info = new ShellInfo(u.FullName, u.Initials, u.Email ?? "", string.Join(", ", roles),
            tenantName, tenantCount, await user.GetPermissionsAsync());
    }
}
