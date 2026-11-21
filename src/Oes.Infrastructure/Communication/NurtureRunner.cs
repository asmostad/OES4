using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Domain.Work;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Security;
using Oes.Infrastructure.Tenancy;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Communication;

/// <summary>
/// Runs due nurture steps. Each enrollment is leased (its NextRunAt pushed forward with a conditional
/// update) before its step runs, so two runs never send the same step. Before every step it checks the
/// stop rules (reply, opportunity closed, workflow off) and consent; texts wait for daytime hours.
/// </summary>
public sealed class NurtureRunner(
    OesDbContext db, TenantContext tenant, IClock clock, TenantTime time, ConsentService consent, MergeFields merge, RecordItems items,
    NotificationService notifications, IMailProvider mail, ISmsProvider sms, AccessTokenService tokens, IConfiguration config, ILogger<NurtureRunner> log)
{
    public const int MaxAttempts = 3;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);
    /// <summary>Texts only go out between 8 am and 8 pm company time.</summary>
    public static readonly TimeOnly TextsFrom = new(8, 0), TextsUntil = new(20, 0);

    public static async Task<int> RunDueAsync(IServiceScopeFactory scopes, int max = 500, CancellationToken ct = default)
    {
        List<(Guid Id, Guid TenantId)> due;
        await using (var s = scopes.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<OesDbContext>();
            var now = s.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            due = (await db.Enrollments.IgnoreQueryFilters([OesDbContext.TenantFilter])
                    .Where(e => e.Status == EnrollmentStatuses.Active && e.NextRunAt <= now)
                    .OrderBy(e => e.NextRunAt).Take(max).Select(e => new { e.Id, e.TenantId }).ToListAsync(ct))
                .Select(e => (e.Id, e.TenantId)).ToList();
        }
        var ran = 0;
        foreach (var (id, tenantId) in due)
        {
            await using var s = scopes.CreateAsyncScope();
            try { if (await s.ServiceProvider.GetRequiredService<NurtureRunner>().RunAsync(tenantId, id, ct)) ran++; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                s.ServiceProvider.GetRequiredService<ILogger<NurtureRunner>>().LogError(ex, "Nurture enrollment {EnrollmentId} failed", id);
            }
        }
        return ran;
    }

    /// <summary>Runs (or reschedules) the enrollment's next step. False when there was nothing to do.</summary>
    public async Task<bool> RunAsync(Guid tenantId, Guid enrollmentId, CancellationToken ct = default)
    {
        tenant.SetTenant(tenantId);
        var now = clock.UtcNow;
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.Id == enrollmentId, ct);
        if (e is null || e.Status != EnrollmentStatuses.Active || e.NextRunAt is not { } due || due > now) return false;
        var leased = await db.Enrollments.Where(x => x.Id == e.Id && x.Status == EnrollmentStatuses.Active && x.NextRunAt == due)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.NextRunAt, now + Lease), ct);
        if (leased != 1) return false;
        await db.Entry(e).ReloadAsync(ct); // so a new NextRunAt equal to the old one is still written

        var seq = await db.Sequences.Include(s => s.Steps).FirstAsync(s => s.Id == e.SequenceId, ct);
        var steps = seq.Steps.OrderBy(s => s.Sort).ToList();
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == e.ContactId, ct);
        var opp = e.OpportunityId is { } oid ? await db.Opportunities.FirstOrDefaultAsync(o => o.Id == oid, ct) : null;

        string? stop = null;
        if (!seq.IsActive) stop = "The workflow was turned off.";
        else if (contact is null || contact.ArchivedAt is not null) stop = "The contact was archived.";
        else if (seq.StopWhenClosed && opp is not null && opp.Outcome != Outcomes.Open) stop = $"The opportunity was {opp.Outcome}.";
        else if (seq.StopOnReply && await RepliedAsync(e, ct)) stop = "They replied.";
        if (stop is not null) return await FinishAsync(e, EnrollmentStatuses.Stopped, stop, ct);
        if (e.StepIndex >= steps.Count) return await FinishAsync(e, EnrollmentStatuses.Completed, null, ct);

        var step = steps[e.StepIndex];
        if (step.Kind == StepKinds.Sms && await NextTextingTimeAsync(now) is { } later)
        {
            e.NextRunAt = later; // quiet hours: wait for the morning
            await db.SaveChangesAsync(ct);
            return false;
        }

        var sender = opp?.OwnerUserId ?? contact!.OwnerUserId ?? e.EnrolledByUserId;
        var (entityType, entityId) = opp is not null ? (EntityTypes.Opportunity, opp.Id) : (EntityTypes.Contact, contact!.Id);
        (string Status, string? Detail, Guid? ItemId) result;
        try
        {
            result = step.Kind switch
            {
                StepKinds.Email => await EmailAsync(seq, step, contact!, sender, entityType, entityId, ct),
                StepKinds.Sms => await TextAsync(seq, step, contact!, sender, entityType, entityId, ct),
                _ => await TaskAsync(step, contact!, sender, entityType, entityId, ct),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Nurture step {StepId} failed", step.Id);
            result = (StepRunStatuses.Failed, "Something went wrong running this step.", null);
        }

        e.Attempts++;
        db.EnrollmentStepRuns.Add(new EnrollmentStepRun
        {
            EnrollmentId = e.Id, StepId = step.Id, StepIndex = e.StepIndex, Kind = step.Kind, Status = result.Status,
            Detail = result.Detail?[..Math.Min(result.Detail.Length, 1000)], Attempt = e.Attempts, ItemId = result.ItemId,
        });
        if (result.Status == StepRunStatuses.Failed && e.Attempts < MaxAttempts)
        {
            e.NextRunAt = clock.UtcNow.AddMinutes(10 * e.Attempts); // retry with backoff
            await db.SaveChangesAsync(ct);
            return true;
        }
        e.StepIndex++;
        e.Attempts = 0;
        if (e.StepIndex >= steps.Count) return await FinishAsync(e, EnrollmentStatuses.Completed, null, ct);
        e.NextRunAt = clock.UtcNow.AddMinutes(steps[e.StepIndex].DelayMinutes);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> FinishAsync(Enrollment e, string status, string? reason, CancellationToken ct)
    {
        e.Status = status; e.StopReason = reason; e.NextRunAt = null; e.FinishedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Any text from the contact (by contact or by one of their numbers) since they were enrolled.</summary>
    private async Task<bool> RepliedAsync(Enrollment e, CancellationToken ct)
    {
        var phones = db.ContactMethods.Where(m => m.ContactId == e.ContactId && (m.Kind == ContactMethodKinds.Mobile || m.Kind == ContactMethodKinds.Phone)).Select(m => m.NormalizedValue);
        return await db.SmsMessages.AnyAsync(m => m.Direction == Directions.Inbound && m.At > e.CreatedAt
                                                   && (m.ContactId == e.ContactId || phones.Contains(m.FromNumber)), ct)
               || await db.EmailMessages.AnyAsync(m => m.Direction == Directions.Inbound && m.SentAt > e.CreatedAt
                                                       && m.Recipients.Any(r => r.ContactId == e.ContactId), ct);
    }

    /// <summary>Null when a text may go out now; otherwise the next allowed time (UTC).</summary>
    private async Task<DateTime?> NextTextingTimeAsync(DateTime nowUtc)
    {
        var local = await time.ToLocalAsync(nowUtc);
        var t = TimeOnly.FromDateTime(local);
        if (t >= TextsFrom && t < TextsUntil) return null;
        var day = DateOnly.FromDateTime(local);
        if (t >= TextsUntil) day = day.AddDays(1);
        return await time.ToUtcAsync(day, new TimeOnly(9, 0));
    }

    private async Task<string?> AddressAsync(Guid contactId, string[] kinds, CancellationToken ct)
    {
        var rows = await db.ContactMethods.Where(m => m.ContactId == contactId && kinds.Contains(m.Kind))
            .Select(m => new { m.NormalizedValue, m.Kind, m.IsPrimary, m.CreatedAt }).ToListAsync(ct);
        return rows.OrderBy(r => Array.IndexOf(kinds, r.Kind)).ThenByDescending(r => r.IsPrimary).ThenBy(r => r.CreatedAt).FirstOrDefault()?.NormalizedValue;
    }

    private async Task<(string, string?, Guid?)> EmailAsync(Sequence seq, SequenceStep step, Contact contact, Guid? sender, string entityType, Guid entityId, CancellationToken ct)
    {
        if (sender is null) return (StepRunStatuses.Skipped, "No one to send it from (no owner).", null);
        var to = await AddressAsync(contact.Id, [ContactMethodKinds.Email], ct);
        if (to is null) return (StepRunStatuses.Skipped, "No email address.", null);
        if (await consent.BlockReasonAsync(contact.Id, Channels.Email, to, seq.Category, ct) is { } why) return (StepRunStatuses.Skipped, why, null);

        var values = await merge.ForAsync(entityType, entityId, contact.Id, sender, ct);
        var subject = MergeFields.Render(step.Subject, values).ReplaceLineEndings(" ");
        var body = MergeFields.Render(step.Body, values);
        var from = await db.Users.Where(u => u.Id == sender).Select(u => new { u.Email, Name = u.FirstName + " " + u.LastName }).FirstAsync(ct);
        var msg = new EmailMessage
        {
            Direction = Directions.Outbound, FromAddress = from.Email ?? "", Subject = subject, Body = body, SentAt = clock.UtcNow, SentByUserId = sender,
            EntityType = entityType, EntityId = entityId, Recipients = [new EmailRecipient { Kind = "to", Address = to, ContactId = contact.Id }],
        };
        string html, text;
        Dictionary<string, string>? headers = null;
        if (seq.Category == MessagePurposes.Marketing)
        {
            var company = await db.Tenants.Where(t => t.Id == contact.TenantId).Select(t => new { t.Name, t.PostalAddress }).FirstAsync(ct);
            if (string.IsNullOrWhiteSpace(company.PostalAddress)) return (StepRunStatuses.Skipped, "Add the company mailing address in Settings: marketing email must include it.", null);
            var token = await tokens.IssueAsync(TokenPurposes.Unsubscribe, "email_message", msg.Id, BlastSender.UnsubscribeLinkLifetime, ct);
            var link = $"{(config["App:BaseUrl"] ?? "").TrimEnd('/')}/portal/u/{token}";
            (html, text) = BlastService.Compose(body, company.Name, company.PostalAddress, link);
            headers = new() { ["List-Unsubscribe"] = $"<{link}>", ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click" };
        }
        else
        {
            (html, text) = (MessagingService.ToHtml(body), body);
        }
        var r = await mail.SendAsync(sender.Value, new OutgoingEmail(from.Email ?? "", from.Name, [to], [], subject, html, text, headers), ct);
        msg.Status = r.Ok ? MessageStatuses.Sent : MessageStatuses.Failed;
        msg.ProviderMessageId = r.ProviderId;
        msg.Error = r.Error;
        db.EmailMessages.Add(msg);
        await items.LinkItemAsync(ItemTypes.Email, msg.Id, entityType, entityId, ct);
        return r.Ok ? (StepRunStatuses.Done, $"Emailed {to}", msg.Id) : (StepRunStatuses.Failed, r.Error, msg.Id);
    }

    private async Task<(string, string?, Guid?)> TextAsync(Sequence seq, SequenceStep step, Contact contact, Guid? sender, string entityType, Guid entityId, CancellationToken ct)
    {
        var fromNumber = await db.TenantPhoneNumbers.Where(p => p.IsActive).Select(p => p.Number).FirstOrDefaultAsync(ct);
        if (fromNumber is null) return (StepRunStatuses.Skipped, "Texting isn't set up.", null);
        var to = await AddressAsync(contact.Id, [ContactMethodKinds.Mobile, ContactMethodKinds.Phone], ct);
        if (to is null) return (StepRunStatuses.Skipped, "No phone number.", null);
        if (await consent.BlockReasonAsync(contact.Id, Channels.Sms, to, seq.Category, ct) is { } why) return (StepRunStatuses.Skipped, why, null);

        var values = await merge.ForAsync(entityType, entityId, contact.Id, sender, ct);
        var body = MergeFields.Render(step.Body, values);
        var first = !await db.SmsMessages.AnyAsync(m => m.ToNumber == to && m.Direction == Directions.Outbound && m.Status == MessageStatuses.Sent, ct);
        if ((first || seq.Category == MessagePurposes.Marketing) && !body.Contains("STOP", StringComparison.OrdinalIgnoreCase))
            body = $"{body}\n{MessagingService.OptOutFooter}";
        var r = await sms.SendAsync(fromNumber, to, body, ct);
        var msg = new SmsMessage
        {
            Direction = Directions.Outbound, FromNumber = fromNumber, ToNumber = to, Body = body, At = clock.UtcNow, SentByUserId = sender,
            Status = r.Ok ? MessageStatuses.Sent : MessageStatuses.Failed, ProviderSid = r.ProviderId, Error = r.Error,
            ContactId = contact.Id, EntityType = entityType, EntityId = entityId,
        };
        db.SmsMessages.Add(msg);
        await items.LinkItemAsync(ItemTypes.Sms, msg.Id, entityType, entityId, ct);
        return r.Ok ? (StepRunStatuses.Done, $"Texted {ContactValue.FormatPhone(to, to)}", msg.Id) : (StepRunStatuses.Failed, r.Error, msg.Id);
    }

    private async Task<(string, string?, Guid?)> TaskAsync(SequenceStep step, Contact contact, Guid? assignee, string entityType, Guid entityId, CancellationToken ct)
    {
        if (assignee is null) return (StepRunStatuses.Skipped, "No one to give the task to (no owner).", null);
        var values = await merge.ForAsync(entityType, entityId, contact.Id, assignee, ct);
        var title = MergeFields.Render(step.Subject, values);
        var task = new TaskItem
        {
            Title = title.Length <= 300 ? title : title[..300],
            Notes = string.IsNullOrWhiteSpace(step.Body) ? null : MergeFields.Render(step.Body, values),
            AssignedToUserId = assignee.Value, EntityType = entityType, EntityId = entityId,
            DueAt = await time.ToUtcAsync(await time.TodayAsync(), new TimeOnly(12, 0)),
        };
        db.Tasks.Add(task);
        await items.LinkItemAsync(ItemTypes.Task, task.Id, entityType, entityId, ct);
        notifications.Notify(assignee, "task.assigned", $"Workflow task: {task.Title}", contact.DisplayName, RecordUrls.For(entityType, entityId) ?? "/Tasks");
        return (StepRunStatuses.Done, $"Task for {contact.DisplayName}", task.Id);
    }
}
