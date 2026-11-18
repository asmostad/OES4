using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Tenancy;
using Oes.Domain.Work;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Work;

/// <summary>In-app notifications (the bell). Saved with the caller's unit of work.</summary>
public sealed class NotificationService(OesDbContext db, ICurrentUser user, IClock clock)
{
    /// <summary>Queues a notification. People aren't notified about their own actions.</summary>
    public void Notify(Guid? userId, string kind, string title, string? body, string? url)
    {
        if (userId is not { } uid || uid == user.UserId) return;
        db.Notifications.Add(new Notification { UserId = uid, Kind = kind, Title = Trim(title, 300)!, Body = Trim(body, 1000), Url = SafeUrl(url) });
    }

    /// <summary>Notifies every active member whose roles grant the permission (e.g. approvers).</summary>
    public async Task NotifyPermissionAsync(string permission, string kind, string title, string? body, string? url, CancellationToken ct = default)
    {
        var userIds = await (from m in db.Memberships where m.Status == MembershipStatus.Active
                             from mr in db.MembershipRoles.Where(r => r.MembershipId == m.Id)
                             from rp in db.RolePermissions.Where(p => p.RoleId == mr.RoleId && p.PermissionKey == permission)
                             select m.UserId).Distinct().ToListAsync(ct);
        foreach (var id in userIds) Notify(id, kind, title, body, url);
    }

    /// <summary>
    /// Queues an automatic alert unless this user already had one with the same key (ever).
    /// Returns true when it was queued. Saved with the caller's unit of work.
    /// </summary>
    public async Task<bool> NotifyOnceAsync(Guid? userId, string dedupeKey, string kind, string title, string? body, string? url, CancellationToken ct = default)
    {
        if (userId is not { } uid) return false;
        var key = Trim(dedupeKey, 200)!;
        if (db.Notifications.Local.Any(n => n.UserId == uid && n.DedupeKey == key)
            || await db.Notifications.AnyAsync(n => n.UserId == uid && n.DedupeKey == key, ct)) return false;
        db.Notifications.Add(new Notification { UserId = uid, Kind = kind, Title = Trim(title, 300)!, Body = Trim(body, 1000), Url = SafeUrl(url), DedupeKey = key });
        return true;
    }

    public async Task<List<Guid>> UsersWithPermissionAsync(string permission, CancellationToken ct = default) =>
        await (from m in db.Memberships where m.Status == MembershipStatus.Active
               from mr in db.MembershipRoles.Where(r => r.MembershipId == m.Id)
               from rp in db.RolePermissions.Where(p => p.RoleId == mr.RoleId && p.PermissionKey == permission)
               select m.UserId).Distinct().ToListAsync(ct);

    public async Task<int> UnreadCountAsync(CancellationToken ct = default) =>
        user.UserId is { } me ? await db.Notifications.CountAsync(n => n.UserId == me && n.ReadAt == null, ct) : 0;

    public async Task<List<Notification>> LatestAsync(int take = 12, CancellationToken ct = default) =>
        user.UserId is { } me
            ? await db.Notifications.Where(n => n.UserId == me).OrderBy(n => n.ReadAt != null).ThenByDescending(n => n.CreatedAt).Take(take).ToListAsync(ct)
            : [];

    /// <summary>Marks one of the current user's notifications read and returns where it points.</summary>
    public async Task<string?> OpenAsync(Guid id, CancellationToken ct = default)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.UserId, ct);
        if (n is null) return null;
        n.ReadAt ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return n.Url;
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        if (user.UserId is not { } me) return;
        var now = clock.UtcNow;
        await db.Notifications.Where(n => n.UserId == me && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    /// <summary>Only app-relative paths are stored, so a notification can never redirect off-site.</summary>
    private static string? SafeUrl(string? url) =>
        url is not null && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? Trim(url, 500) : null;
}
