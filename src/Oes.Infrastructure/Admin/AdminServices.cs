using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Admin;

public sealed class AdminRuleException(string message) : InvalidOperationException(message);

public sealed record MemberRow(
    Guid MembershipId, Guid UserId, string FullName, string Email, string? Title, MembershipStatus Status,
    bool HasPassword, DateTime? LastSignInAt, IReadOnlyList<Guid> RoleIds, IReadOnlyList<string> RoleNames);

public sealed record RoleRow(Guid Id, string Name, string? Description, bool IsSystem, int PermissionCount, int MemberCount);

public sealed record InviteRequest(string Email, string FirstName, string LastName, IReadOnlyList<Guid> RoleIds);

/// <summary>
/// User and role administration for the current tenant. Everything here is tenant-scoped
/// by the global filter; the rules that protect against lock-out live here, not in pages.
/// </summary>
public sealed class TenantAdminService(
    OesDbContext db, UserManager<AppUser> users, ITenantContext tenant, ICurrentUser me,
    IAuditLog audit, ISystemMailer mailer)
{
    public async Task<List<MemberRow>> MembersAsync(CancellationToken ct = default)
    {
        var rows = await (from m in db.Memberships
                          join u in db.Users on m.UserId equals u.Id
                          orderby u.FirstName, u.LastName
                          select new
                          {
                              m.Id, m.UserId, u.FirstName, u.LastName, u.Email, m.Title, m.Status,
                              HasPassword = u.PasswordHash != null, m.LastSignInAt,
                              Roles = m.Roles.Select(r => new { r.RoleId, r.Role!.Name }).ToList(),
                          }).ToListAsync(ct);

        return rows.Select(r => new MemberRow(r.Id, r.UserId, $"{r.FirstName} {r.LastName}".Trim(), r.Email ?? "",
            r.Title, r.Status, r.HasPassword, r.LastSignInAt,
            r.Roles.Select(x => x.RoleId).ToList(), r.Roles.Select(x => x.Name).OrderBy(n => n).ToList())).ToList();
    }

    public Task<List<RoleRow>> RolesAsync(CancellationToken ct = default) =>
        db.Roles.OrderBy(r => r.Name)
            .Select(r => new RoleRow(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions.Count,
                db.MembershipRoles.Count(mr => mr.RoleId == r.Id)))
            .ToListAsync(ct);

    public async Task InviteAsync(InviteRequest req, string baseUrl, CancellationToken ct = default)
    {
        var email = req.Email.Trim();
        await EnsureRolesExistAsync(req.RoleIds, ct);

        var user = await users.FindByEmailAsync(email);
        var isNewUser = user is null;
        if (user is null)
        {
            user = new AppUser { UserName = email, Email = email, FirstName = req.FirstName.Trim(), LastName = req.LastName.Trim() };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded) throw new AdminRuleException(string.Join(" ", created.Errors.Select(e => e.Description)));
        }
        else if (await db.Memberships.AnyAsync(m => m.UserId == user.Id, ct))
        {
            throw new AdminRuleException("That person is already a user in this company.");
        }

        var membership = new TenantMembership { UserId = user.Id, Status = MembershipStatus.Active };
        membership.Roles.AddRange(req.RoleIds.Distinct().Select(id => new MembershipRole { RoleId = id }));
        db.Memberships.Add(membership);
        audit.Record("user.invited", "User", user.Id, $"Invited {email}");
        await db.SaveChangesAsync(ct);

        var tenantName = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.Name).FirstAsync(ct);
        var enc = HtmlEncoder.Default;
        string body, link;
        if (isNewUser)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            link = $"{baseUrl.TrimEnd('/')}/Account/ResetPassword?userId={user.Id}&code={code}";
            body = $"<p>Hi {enc.Encode(user.FirstName)},</p><p>You've been invited to <strong>{enc.Encode(tenantName)}</strong> on OES.</p>" +
                   $"<p><a href=\"{enc.Encode(link)}\">Set your password</a> to get started. The link works for 24 hours.</p>";
        }
        else
        {
            link = $"{baseUrl.TrimEnd('/')}/Account/Login";
            body = $"<p>Hi {enc.Encode(user.FirstName)},</p><p>You've been added to <strong>{enc.Encode(tenantName)}</strong> on OES. " +
                   $"Sign in with your existing account and choose it from the company list.</p><p><a href=\"{enc.Encode(link)}\">Sign in</a></p>";
        }
        await mailer.SendAsync(new SystemEmail(email, $"You've been invited to {tenantName} on OES", body, link), ct);
    }

    public async Task SetMemberRolesAsync(Guid membershipId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        await EnsureRolesExistAsync(roleIds, ct);
        var m = await db.Memberships.Include(x => x.Roles).FirstOrDefaultAsync(x => x.Id == membershipId, ct)
            ?? throw new AdminRuleException("User not found.");

        db.MembershipRoles.RemoveRange(m.Roles.Where(r => !roleIds.Contains(r.RoleId)));
        foreach (var id in roleIds.Distinct().Where(id => m.Roles.All(r => r.RoleId != id)))
            m.Roles.Add(new MembershipRole { RoleId = id });

        audit.Record("user.roles_changed", "User", m.UserId, details: new { roleIds });
        await GuardAdminsRemainAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetMemberStatusAsync(Guid membershipId, MembershipStatus status, CancellationToken ct = default)
    {
        var m = await db.Memberships.FirstOrDefaultAsync(x => x.Id == membershipId, ct)
            ?? throw new AdminRuleException("User not found.");
        if (m.UserId == me.UserId && status != MembershipStatus.Active)
            throw new AdminRuleException("You can't disable your own access.");

        m.Status = status;
        audit.Record(status == MembershipStatus.Active ? "user.enabled" : "user.disabled", "User", m.UserId);
        await GuardAdminsRemainAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Role> GetRoleAsync(Guid roleId, CancellationToken ct = default) =>
        await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == roleId, ct)
        ?? throw new AdminRuleException("Role not found.");

    public async Task<Guid> SaveRoleAsync(Guid? roleId, string name, string? description, IReadOnlyCollection<string> permissionKeys,
        CancellationToken ct = default)
    {
        var unknown = permissionKeys.Where(k => !Permissions.IsKnown(k)).ToList();
        if (unknown.Count > 0) throw new AdminRuleException($"Unknown permissions: {string.Join(", ", unknown)}");
        name = name.Trim();
        if (name.Length == 0) throw new AdminRuleException("Role name is required.");
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != roleId, ct))
            throw new AdminRuleException("Another role already has that name.");

        Role role;
        if (roleId is { } id)
        {
            role = await GetRoleAsync(id, ct);
            db.RolePermissions.RemoveRange(role.Permissions.Where(p => !permissionKeys.Contains(p.PermissionKey)));
            foreach (var key in permissionKeys.Where(k => role.Permissions.All(p => p.PermissionKey != k)))
                role.Permissions.Add(new RolePermission { PermissionKey = key });
        }
        else
        {
            role = new Role { Name = name, Permissions = permissionKeys.Select(k => new RolePermission { PermissionKey = k }).ToList() };
            db.Roles.Add(role);
        }
        role.Name = name;
        role.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        audit.Record(roleId is null ? "role.created" : "role.updated", "Role", role.Id, $"Role {name}",
            new { permissions = permissionKeys.Order().ToList() });
        await GuardAdminsRemainAsync(ct);
        await db.SaveChangesAsync(ct);
        return role.Id;
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await GetRoleAsync(roleId, ct);
        if (role.IsSystem) throw new AdminRuleException("Built-in roles can be edited but not deleted.");
        if (await db.MembershipRoles.AnyAsync(mr => mr.RoleId == roleId, ct))
            throw new AdminRuleException("Remove this role from its users before deleting it.");
        db.Roles.Remove(role);
        audit.Record("role.deleted", "Role", role.Id, $"Role {role.Name}");
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureRolesExistAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        var found = await db.Roles.CountAsync(r => roleIds.Contains(r.Id), ct);
        if (found != roleIds.Distinct().Count()) throw new AdminRuleException("One or more roles were not found.");
    }

    /// <summary>
    /// Refuses a change that would leave the company with nobody able to manage users and roles.
    /// Evaluated against pending (unsaved) changes in the change tracker.
    /// </summary>
    private async Task GuardAdminsRemainAsync(CancellationToken ct)
    {
        var saved = await db.Memberships.AsNoTracking().Select(m => new { m.Id, m.Status }).ToListAsync(ct);
        var pending = db.ChangeTracker.Entries<TenantMembership>().ToDictionary(e => e.Entity.Id, e => e.Entity.Status);
        var activeMemberIds = saved.Select(m => (m.Id, Status: pending.GetValueOrDefault(m.Id, m.Status)))
            .Concat(pending.Where(p => saved.All(s => s.Id != p.Key)).Select(p => (Id: p.Key, Status: p.Value)))
            .Where(x => x.Status == MembershipStatus.Active).Select(x => x.Id).ToHashSet();

        var membershipRoles = (await db.MembershipRoles.AsNoTracking().ToListAsync(ct))
            .Where(mr => db.ChangeTracker.Entries<MembershipRole>().All(e => e.Entity.Id != mr.Id || e.State != EntityState.Deleted))
            .Concat(db.ChangeTracker.Entries<MembershipRole>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .ToList();

        var rolePerms = (await db.RolePermissions.AsNoTracking().ToListAsync(ct))
            .Where(rp => db.ChangeTracker.Entries<RolePermission>().All(e => e.Entity.Id != rp.Id || e.State != EntityState.Deleted))
            .Concat(db.ChangeTracker.Entries<RolePermission>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .ToList();

        bool AnyoneHas(string key)
        {
            var roles = rolePerms.Where(p => p.PermissionKey == key).Select(p => p.RoleId).ToHashSet();
            return membershipRoles.Any(mr => roles.Contains(mr.RoleId) && activeMemberIds.Contains(mr.MembershipId));
        }

        if (!AnyoneHas(Permissions.UsersManage) || !AnyoneHas(Permissions.RolesManage))
            throw new AdminRuleException("At least one active user must keep the Users and Roles permissions, or nobody could manage access.");
    }
}
