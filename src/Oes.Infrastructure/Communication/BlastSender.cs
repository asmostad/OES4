using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Security;
using Oes.Infrastructure.Tenancy;

namespace Oes.Infrastructure.Communication;

/// <summary>
/// Sends due blasts in throttled batches. Each run sends at most ThrottlePerMinute emails per blast;
/// the worker calls <see cref="RunDueAsync"/> every minute. A recipient is claimed (pending → sending)
/// with a conditional update before it's sent, so two runs can never send the same email.
/// </summary>
public sealed class BlastSender(
    OesDbContext db, TenantContext tenant, IClock clock, ConsentService consent, MergeFields merge, RecordItems items,
    IMailProvider mail, AccessTokenService tokens, IConfiguration config, ILogger<BlastSender> log)
{
    public static readonly TimeSpan UnsubscribeLinkLifetime = TimeSpan.FromDays(400);
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    /// <summary>Finds due blasts across all companies and sends one batch of each, each in its own scope.</summary>
    public static async Task<int> RunDueAsync(IServiceScopeFactory scopes, CancellationToken ct = default)
    {
        List<(Guid Id, Guid TenantId)> due;
        await using (var s = scopes.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<OesDbContext>();
            var now = s.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            due = (await db.Blasts.IgnoreQueryFilters([OesDbContext.TenantFilter])
                    .Where(b => (b.Status == BlastStatuses.Scheduled || b.Status == BlastStatuses.Sending) && b.ScheduledAt <= now)
                    .OrderBy(b => b.ScheduledAt).Select(b => new { b.Id, b.TenantId }).ToListAsync(ct))
                .Select(b => (b.Id, b.TenantId)).ToList();
        }
        var sent = 0;
        foreach (var (id, tenantId) in due)
        {
            await using var s = scopes.CreateAsyncScope();
            try { sent += await s.ServiceProvider.GetRequiredService<BlastSender>().SendBatchAsync(tenantId, id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                s.ServiceProvider.GetRequiredService<ILogger<BlastSender>>().LogError(ex, "Blast {BlastId} batch failed", id);
            }
        }
        return sent;
    }

    public async Task<int> SendBatchAsync(Guid tenantId, Guid blastId, CancellationToken ct = default)
    {
        tenant.SetTenant(tenantId);
        var b = await db.Blasts.FirstOrDefaultAsync(x => x.Id == blastId, ct);
        if (b is null || b.Status is not (BlastStatuses.Scheduled or BlastStatuses.Sending)) return 0;
        var now = clock.UtcNow;
        if (b.Status == BlastStatuses.Scheduled) { b.Status = BlastStatuses.Sending; b.StartedAt = now; }

        // A run that died mid-send: we can't know whether the email went, so don't send it twice.
        var stuck = await db.BlastRecipients.Where(r => r.BlastId == b.Id && r.Status == BlastRecipientStatuses.Sending && r.ClaimedAt < now - StuckAfter).ToListAsync(ct);
        foreach (var r in stuck) { r.Status = BlastRecipientStatuses.Failed; r.Error = "Interrupted while sending; not retried to avoid a duplicate."; b.FailedCount++; }
        await db.SaveChangesAsync(ct);

        var candidates = await db.BlastRecipients.Where(r => r.BlastId == b.Id && r.Status == BlastRecipientStatuses.Pending)
            .OrderBy(r => r.Id).Select(r => r.Id).Take(b.ThrottlePerMinute).ToListAsync(ct);
        var claimed = new List<Guid>();
        foreach (var id in candidates)
        {
            var n = await db.BlastRecipients.Where(r => r.Id == id && r.Status == BlastRecipientStatuses.Pending)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, BlastRecipientStatuses.Sending).SetProperty(r => r.ClaimedAt, now), ct);
            if (n == 1) claimed.Add(id);
        }

        var company = await db.Tenants.Where(t => t.Id == tenantId).Select(t => new { t.Name, t.PostalAddress }).FirstAsync(ct);
        var from = await db.Users.Where(u => u.Id == b.SenderUserId).Select(u => new { u.Email, Name = u.FirstName + " " + u.LastName }).FirstAsync(ct);
        var baseUrl = (config["App:BaseUrl"] ?? "").TrimEnd('/');
        var rows = await db.BlastRecipients.Where(r => claimed.Contains(r.Id)).ToListAsync(ct);
        // Consent can change between scheduling and sending: check again.
        var blocked = await consent.BlockReasonsAsync(rows.Select(r => (r.ContactId, r.Address)).ToList(), Channels.Email, MessagePurposes.Marketing, ct);
        var sent = 0;
        foreach (var r in rows)
        {
            if (blocked.TryGetValue((r.ContactId, r.Address), out var why))
            {
                r.Status = BlastRecipientStatuses.Skipped; r.Error = why; b.SkippedCount++;
                await db.SaveChangesAsync(ct);
                continue;
            }
            var token = await tokens.IssueAsync(TokenPurposes.Unsubscribe, "blast_recipient", r.Id, UnsubscribeLinkLifetime, ct);
            var link = $"{baseUrl}/portal/u/{token}";
            var values = await merge.ForAsync(EntityTypes.Contact, r.ContactId, r.ContactId, b.SenderUserId, ct);
            var subject = MergeFields.Render(b.Subject, values).ReplaceLineEndings(" ");
            var body = MergeFields.Render(b.Body, values);
            var (html, text) = BlastService.Compose(body, company.Name, company.PostalAddress, link);
            var headers = new Dictionary<string, string> { ["List-Unsubscribe"] = $"<{link}>", ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click" };

            ProviderResult result;
            try { result = await mail.SendAsync(b.SenderUserId, new OutgoingEmail(from.Email ?? "", from.Name, [r.Address], [], subject, html, text, headers), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Blast {BlastId} send to recipient {RecipientId} failed", b.Id, r.Id);
                result = new ProviderResult(false, null, "The mail provider failed.");
            }

            var msg = new EmailMessage
            {
                Direction = Directions.Outbound, FromAddress = from.Email ?? "", Subject = subject, Body = body, SentAt = clock.UtcNow,
                SentByUserId = b.SenderUserId, Status = result.Ok ? MessageStatuses.Sent : MessageStatuses.Failed,
                ProviderMessageId = result.ProviderId, Error = result.Error, TemplateId = b.TemplateId, BlastId = b.Id,
                Recipients = [new EmailRecipient { Kind = "to", Address = r.Address, ContactId = r.ContactId }],
            };
            db.EmailMessages.Add(msg);
            await items.LinkItemAsync(ItemTypes.Email, msg.Id, EntityTypes.Contact, r.ContactId, ct);
            r.EmailMessageId = msg.Id;
            r.SentAt = msg.SentAt;
            if (result.Ok) { r.Status = BlastRecipientStatuses.Sent; b.SentCount++; sent++; }
            else { r.Status = BlastRecipientStatuses.Failed; r.Error = result.Error; b.FailedCount++; }
            await db.SaveChangesAsync(ct);
        }

        if (!await db.BlastRecipients.AnyAsync(r => r.BlastId == b.Id && (r.Status == BlastRecipientStatuses.Pending || r.Status == BlastRecipientStatuses.Sending), ct))
        {
            b.Status = BlastStatuses.Sent;
            b.FinishedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }
}
