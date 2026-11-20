using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Domain.Work;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Infrastructure.Work;

public sealed record PushRunResult(int Notifications, int Sent, int Failed, int Disabled);

/// <summary>
/// Phone push notifications. The field app registers its Expo push address after the user allows notifications;
/// the worker (every 30 seconds) sends each new notification of a <see cref="PushKinds"/> kind to the user's phones,
/// with the unread count as the app badge. Phones Expo reports as gone are switched off. With pushes off
/// (no provider configured), notifications are still marked handled, so turning pushes on later doesn't send a backlog.
/// </summary>
public sealed partial class PushService(OesDbContext db, ICurrentUser user, TenantContext tenant, IPushSender sender, IClock clock,
    ILogger<PushService> log)
{
    /// <summary>Only notifications this recent are pushed: an old one would just be noise.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    [GeneratedRegex(@"^Expo(nent)?PushToken\[[A-Za-z0-9_\-]{10,180}\]$")]
    private static partial Regex ValidToken();

    /// <summary>Records this phone for the signed-in user (a phone that changed hands moves to the new user).</summary>
    public async Task RegisterAsync(string token, string platform, CancellationToken ct = default)
    {
        var me = user.UserId ?? throw new RecordForbiddenException("Sign in first.");
        token = (token ?? "").Trim();
        if (!ValidToken().IsMatch(token)) throw new CrmRuleException("That isn't an Expo push token.");
        var kind = (platform ?? "").Trim().ToLowerInvariant();
        if (kind is not ("ios" or "android")) throw new CrmRuleException("Platform must be ios or android.");
        var row = await db.DevicePushTokens.FirstOrDefaultAsync(d => d.Token == token, ct);
        if (row is null) db.DevicePushTokens.Add(row = new DevicePushToken { Token = token });
        row.UserId = me;
        row.Platform = kind;
        row.LastSeenAt = clock.UtcNow;
        row.DisabledAt = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Forgets this phone for the signed-in user (on sign-out).</summary>
    public async Task UnregisterAsync(string token, CancellationToken ct = default)
    {
        var me = user.UserId ?? throw new RecordForbiddenException("Sign in first.");
        await db.DevicePushTokens.Where(d => d.UserId == me && d.Token == (token ?? "").Trim()).ExecuteDeleteAsync(ct);
    }

    /// <summary>Every company with notifications waiting, one scope each.</summary>
    public static async Task<PushRunResult> RunAllAsync(IServiceScopeFactory scopes, CancellationToken ct = default)
    {
        List<Guid> tenants;
        await using (var s = scopes.CreateAsyncScope())
        {
            var since = s.ServiceProvider.GetRequiredService<IClock>().UtcNow - Window;
            tenants = await s.ServiceProvider.GetRequiredService<OesDbContext>().Notifications.IgnoreQueryFilters([OesDbContext.TenantFilter])
                .Where(n => n.PushedAt == null && n.CreatedAt >= since && PushKinds.All.Contains(n.Kind))
                .Select(n => n.TenantId).Distinct().ToListAsync(ct);
        }
        var total = new PushRunResult(0, 0, 0, 0);
        foreach (var t in tenants)
        {
            await using var s = scopes.CreateAsyncScope();
            try
            {
                var r = await s.ServiceProvider.GetRequiredService<PushService>().RunAsync(t, ct);
                total = new PushRunResult(total.Notifications + r.Notifications, total.Sent + r.Sent, total.Failed + r.Failed, total.Disabled + r.Disabled);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                s.ServiceProvider.GetRequiredService<ILogger<PushService>>().LogError(ex, "Push failed for tenant {TenantId}", t);
            }
        }
        return total;
    }

    public async Task<PushRunResult> RunAsync(Guid tenantId, CancellationToken ct = default)
    {
        tenant.SetTenant(tenantId);
        var now = clock.UtcNow;
        var since = now - Window;
        var pending = await db.Notifications.Where(n => n.PushedAt == null && n.CreatedAt >= since && PushKinds.All.Contains(n.Kind))
            .OrderBy(n => n.CreatedAt).Take(500).ToListAsync(ct);
        if (pending.Count == 0) return new PushRunResult(0, 0, 0, 0);

        var users = pending.Select(n => n.UserId).Distinct().ToList();
        var tokens = sender.IsConfigured
            ? await db.DevicePushTokens.Where(d => users.Contains(d.UserId) && d.DisabledAt == null).ToListAsync(ct)
            : [];
        var unread = tokens.Count == 0 ? new Dictionary<Guid, int>()
            : await db.Notifications.Where(n => users.Contains(n.UserId) && n.ReadAt == null).GroupBy(n => n.UserId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var messages = new List<(PushMessage Message, DevicePushToken Token)>();
        foreach (var n in pending)
            foreach (var t in tokens.Where(t => t.UserId == n.UserId))
                messages.Add((new PushMessage(t.Token, n.Title, n.Body,
                    new Dictionary<string, string> { ["url"] = n.Url ?? "", ["kind"] = n.Kind, ["id"] = n.Id.ToString() },
                    unread.GetValueOrDefault(n.UserId)), t));

        int sent = 0, failed = 0, disabled = 0;
        if (messages.Count > 0)
        {
            var tickets = await sender.SendAsync(messages.Select(m => m.Message).ToList(), ct);
            for (var i = 0; i < messages.Count; i++)
            {
                var ticket = i < tickets.Count ? tickets[i] : new PushTicket(false, false, "No answer.");
                if (ticket.Ok) sent++;
                else
                {
                    failed++;
                    if (ticket.DeviceGone && messages[i].Token.DisabledAt is null) { messages[i].Token.DisabledAt = now; disabled++; }
                    else log.LogWarning("Push to a {Platform} phone failed: {Error}", messages[i].Token.Platform, ticket.Error);
                }
            }
        }
        // Handled either way: a push isn't retried (the bell still has it), so a phone never gets the same one twice.
        foreach (var n in pending) n.PushedAt = now;
        await db.SaveChangesAsync(ct);
        return new PushRunResult(pending.Count, sent, failed, disabled);
    }
}
