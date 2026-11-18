using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Common;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Security;
using Oes.Infrastructure.Tenancy;

namespace Oes.Infrastructure.Communication;

public sealed class BlastInput
{
    public string Name { get; set; } = "";
    public Guid? TemplateId { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public BlastAudience Audience { get; set; } = new();
    public int ThrottlePerMinute { get; set; } = 30;
}

public sealed record BlastRow(Guid Id, string Name, string Status, DateTime? ScheduledAt, int Total, int Sent, int Failed, int Skipped, DateTime CreatedAt, string? SenderName);
public sealed record AudiencePreview(int Eligible, int Blocked, int NoEmail, List<string> Sample);
public sealed record BlastRecipientRow(Guid Id, Guid ContactId, string ContactName, string Address, string Status, DateTime? SentAt, string? Error);
public sealed record UnsubscribeView(string Company, string MaskedAddress, bool Done);

/// <summary>
/// Marketing email blasts. The audience is resolved into recipients when the blast is scheduled
/// (blocked contacts are recorded as skipped); the worker's <see cref="BlastSender"/> sends in
/// throttled batches and checks consent again just before each send.
/// </summary>
public sealed class BlastService(
    OesDbContext db, RecordScope scope, ICurrentUser user, IClock clock, TenantContext tenant,
    ConsentService consent, MergeFields merge, IMailProvider mail, AccessTokenService tokens)
{
    public const int MaxThrottle = 200;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static BlastAudience Audience(Blast b) => JsonSerializer.Deserialize<BlastAudience>(b.AudienceJson, Json) ?? new BlastAudience();

    public async Task<List<BlastRow>> ListAsync(CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BlastsManage);
        return await (from b in db.Blasts
                      join u in db.Users on b.SenderUserId equals u.Id into us from u in us.DefaultIfEmpty()
                      orderby b.CreatedAt descending
                      select new BlastRow(b.Id, b.Name, b.Status, b.ScheduledAt, b.Total, b.SentCount, b.FailedCount, b.SkippedCount, b.CreatedAt,
                          u == null ? null : u.FirstName + " " + u.LastName)).Take(200).ToListAsync(ct);
    }

    public async Task<Blast> GetAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BlastsManage);
        return await db.Blasts.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new RecordNotFoundException("Blast");
    }

    public async Task<Guid> SaveDraftAsync(Guid? id, BlastInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BlastsManage);
        var me = user.UserId ?? throw new RecordForbiddenException("Sign in first.");
        var name = input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 150) throw new CrmRuleException("Give the blast a name (up to 150 characters).");
        var subject = input.Subject?.Trim() ?? "";
        if (subject.Length is 0 or > 300) throw new CrmRuleException("Add a subject (up to 300 characters).");
        var body = input.Body?.Trim() ?? "";
        if (body.Length is 0 or > 100_000) throw new CrmRuleException("Write the message.");
        var unknown = MergeFields.Unknown(subject).Concat(MergeFields.Unknown(body)).Distinct().ToList();
        if (unknown.Count > 0) throw new CrmRuleException($"Unknown merge field: {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}.");
        if (input.ThrottlePerMinute is < 1 or > MaxThrottle) throw new CrmRuleException($"Send between 1 and {MaxThrottle} emails a minute.");
        var audience = input.Audience ?? new BlastAudience();
        audience.Statuses = audience.Statuses.Where(s => s is ContactStatuses.Lead or ContactStatuses.Customer or ContactStatuses.PastCustomer).Distinct().ToList();
        audience.SourceKey = string.IsNullOrWhiteSpace(audience.SourceKey) ? null : audience.SourceKey.Trim();
        if (input.TemplateId is { } tid && !await db.MessageTemplates.AnyAsync(t => t.Id == tid && t.Channel == Channels.Email, ct))
            throw new RecordNotFoundException("Template");

        Blast b;
        if (id is { } existing)
        {
            b = await GetAsync(existing, ct);
            if (b.Status != BlastStatuses.Draft) throw new CrmRuleException("Only drafts can be edited.");
        }
        else
        {
            b = db.Blasts.Add(new Blast { SenderUserId = me, CreatedByUserId = me }).Entity;
        }
        b.Name = name; b.TemplateId = input.TemplateId; b.Subject = subject; b.Body = body;
        b.AudienceJson = JsonSerializer.Serialize(audience, Json); b.ThrottlePerMinute = input.ThrottlePerMinute;
        await db.SaveChangesAsync(ct);
        return b.Id;
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        var b = await GetAsync(id, ct);
        if (b.Status != BlastStatuses.Draft) throw new CrmRuleException("Only drafts can be deleted. Cancel it instead.");
        db.Blasts.Remove(b);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Audience ----------------

    private sealed record Candidate(Guid ContactId, string Name, string? Address);

    private async Task<List<Candidate>> CandidatesAsync(BlastAudience a, CancellationToken ct)
    {
        var contacts = db.Contacts.Where(c => c.ArchivedAt == null);
        if (!a.IncludeOrganizations) contacts = contacts.Where(c => c.Type == ContactTypes.Person);
        if (a.Statuses.Count > 0) contacts = contacts.Where(c => a.Statuses.Contains(c.Status));
        if (a.SourceKey is { } src) contacts = contacts.Where(c => c.SourceKey == src);
        if (a.StormEventId is { } storm)
        {
            var onStorm = db.Opportunities.Where(o => o.StormEventId == storm).Select(o => o.PrimaryContactId);
            contacts = contacts.Where(c => onStorm.Contains(c.Id));
        }
        var people = await contacts.OrderBy(c => c.DisplayName).Select(c => new { c.Id, c.DisplayName }).ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var emails = new Dictionary<Guid, string>();
        foreach (var chunk in ids.Chunk(500))
        {
            var rows = await db.ContactMethods.Where(m => m.Kind == ContactMethodKinds.Email && chunk.Contains(m.ContactId))
                .Select(m => new { m.ContactId, m.NormalizedValue, m.IsPrimary, m.CreatedAt }).ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.ContactId))
                emails[g.Key] = g.OrderByDescending(r => r.IsPrimary).ThenBy(r => r.CreatedAt).First().NormalizedValue;
        }
        return people.Select(p => new Candidate(p.Id, p.DisplayName, emails.GetValueOrDefault(p.Id))).ToList();
    }

    /// <summary>Splits candidates into who gets it (one email per address) and who is skipped, and why.</summary>
    private async Task<(List<Candidate> Send, List<(Candidate C, string Reason)> Skip, int NoEmail)> ResolveAsync(BlastAudience a, CancellationToken ct)
    {
        var all = await CandidatesAsync(a, ct);
        var withEmail = all.Where(c => c.Address is not null).ToList();
        var blocked = await consent.BlockReasonsAsync(withEmail.Select(c => (c.ContactId, c.Address!)).ToList(), Channels.Email, MessagePurposes.Marketing, ct);
        var send = new List<Candidate>();
        var skip = new List<(Candidate, string)>();
        var seen = new HashSet<string>();
        foreach (var c in withEmail)
        {
            if (blocked.TryGetValue((c.ContactId, c.Address!), out var why)) skip.Add((c, why));
            else if (!seen.Add(c.Address!)) skip.Add((c, "Same address as another contact in this blast."));
            else send.Add(c);
        }
        return (send, skip, all.Count - withEmail.Count);
    }

    public async Task<AudiencePreview> PreviewAsync(BlastAudience audience, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BlastsManage);
        var (send, skip, noEmail) = await ResolveAsync(audience, ct);
        return new AudiencePreview(send.Count, skip.Count, noEmail, send.Take(8).Select(c => $"{c.Name} <{c.Address}>").ToList());
    }

    // ---------------- Scheduling ----------------

    /// <summary>Locks the audience in and queues the blast. <paramref name="sendAtUtc"/> null = as soon as possible.</summary>
    public async Task<int> ScheduleAsync(Guid id, DateTime? sendAtUtc, CancellationToken ct = default)
    {
        var b = await GetAsync(id, ct);
        if (b.Status != BlastStatuses.Draft) throw new CrmRuleException("This blast was already scheduled.");
        var address = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => t.PostalAddress).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(address))
            throw new CrmRuleException("Add the company's mailing address in Settings first. Marketing email must include it.");
        var now = clock.UtcNow;
        if (sendAtUtc is { } at && (at < now.AddMinutes(-5) || at > now.AddDays(90))) throw new CrmRuleException("Pick a time in the next 90 days.");

        var (send, skip, _) = await ResolveAsync(Audience(b), ct);
        if (send.Count == 0) throw new CrmRuleException("No one in this audience can be emailed.");
        foreach (var c in send) db.BlastRecipients.Add(new BlastRecipient { BlastId = b.Id, ContactId = c.ContactId, Address = c.Address! });
        foreach (var (c, why) in skip)
            db.BlastRecipients.Add(new BlastRecipient { BlastId = b.Id, ContactId = c.ContactId, Address = c.Address!, Status = BlastRecipientStatuses.Skipped, Error = why });
        b.Status = BlastStatuses.Scheduled;
        b.ScheduledAt = sendAtUtc ?? now;
        b.Total = send.Count;
        b.SkippedCount = skip.Count;
        await db.SaveChangesAsync(ct);
        return send.Count;
    }

    public async Task CancelAsync(Guid id, CancellationToken ct = default)
    {
        var b = await GetAsync(id, ct);
        if (b.Status is not (BlastStatuses.Scheduled or BlastStatuses.Sending)) throw new CrmRuleException("Only a scheduled or sending blast can be cancelled.");
        b.Status = BlastStatuses.Cancelled;
        b.FinishedAt = clock.UtcNow;
        var pending = await db.BlastRecipients.Where(r => r.BlastId == id && r.Status == BlastRecipientStatuses.Pending).ToListAsync(ct);
        foreach (var r in pending) { r.Status = BlastRecipientStatuses.Skipped; r.Error = "Cancelled."; }
        b.SkippedCount += pending.Count;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<BlastRecipientRow>> RecipientsAsync(Guid id, string? status, int take = 500, CancellationToken ct = default)
    {
        await GetAsync(id, ct);
        var q = from r in db.BlastRecipients where r.BlastId == id
                join c in db.Contacts on r.ContactId equals c.Id
                select new { r, c.DisplayName };
        if (!string.IsNullOrEmpty(status)) q = q.Where(x => x.r.Status == status);
        return (await q.OrderBy(x => x.DisplayName).Take(take).ToListAsync(ct))
            .Select(x => new BlastRecipientRow(x.r.Id, x.r.ContactId, x.DisplayName, x.r.Address, x.r.Status, x.r.SentAt, x.r.Error)).ToList();
    }

    /// <summary>Sends the draft to the signed-in user, merged as the first person in the audience. Nothing is recorded.</summary>
    public async Task<string> SendTestAsync(Guid id, CancellationToken ct = default)
    {
        var b = await GetAsync(id, ct);
        var me = user.UserId ?? throw new RecordForbiddenException("Sign in first.");
        var myEmail = await db.Users.Where(u => u.Id == me).Select(u => u.Email).FirstAsync(ct) ?? throw new CrmRuleException("Your account has no email address.");
        var sample = (await ResolveAsync(Audience(b), ct)).Send.FirstOrDefault();
        var company = await db.Tenants.Where(t => t.Id == tenant.TenantId).Select(t => new { t.Name, t.PostalAddress }).FirstAsync(ct);
        var values = await merge.ForAsync(EntityTypes.Contact, sample?.ContactId, sample?.ContactId, b.SenderUserId, ct);
        var (html, text) = Compose(MergeFields.Render(b.Body, values), company.Name, company.PostalAddress, "#unsubscribe-link-goes-here");
        var r = await mail.SendAsync(me, new OutgoingEmail(myEmail, null, [myEmail], [], "[Test] " + MergeFields.Render(b.Subject, values).ReplaceLineEndings(" "), html, text), ct);
        if (!r.Ok) throw new CrmRuleException(r.Error ?? "The test couldn't be sent.");
        return myEmail;
    }

    /// <summary>Body plus the required footer: who sent it, their postal address and how to unsubscribe.</summary>
    public static (string Html, string Text) Compose(string body, string company, string? postalAddress, string unsubscribeUrl)
    {
        var footerText = $"\n\n--\n{company}{(string.IsNullOrWhiteSpace(postalAddress) ? "" : " · " + postalAddress.ReplaceLineEndings(", "))}\n" +
                         $"You're getting this because you're a contact of {company}. Unsubscribe: {unsubscribeUrl}";
        static string enc(string s) => System.Net.WebUtility.HtmlEncode(s);
        var footerHtml = "<div style=\"font-family:Arial,sans-serif;font-size:12px;color:#6b7280;margin-top:24px;border-top:1px solid #e5e7eb;padding-top:12px\">" +
                         $"{enc(company)}{(string.IsNullOrWhiteSpace(postalAddress) ? "" : " · " + enc(postalAddress.ReplaceLineEndings(", ")))}<br>" +
                         $"You're getting this because you're a contact of {enc(company)}. <a href=\"{enc(unsubscribeUrl)}\">Unsubscribe</a></div>";
        return (MessagingService.ToHtml(body) + footerHtml, body + footerText);
    }

    // ---------------- Unsubscribe (public link) ----------------

    private async Task<(BlastRecipient R, ExternalAccessToken T)?> ResolveTokenAsync(string? raw, CancellationToken ct)
    {
        var t = await tokens.ResolveAsync(raw, TokenPurposes.Unsubscribe, ct);
        if (t is null || t.EntityType != "blast_recipient") return null;
        var r = await db.BlastRecipients.FirstOrDefaultAsync(x => x.Id == t.EntityId, ct);
        return r is null ? null : (r, t);
    }

    public async Task<UnsubscribeView?> UnsubscribeViewAsync(string? raw, CancellationToken ct = default)
    {
        if (await ResolveTokenAsync(raw, ct) is not { } x) return null;
        var company = await db.Tenants.Where(t => t.Id == x.R.TenantId).Select(t => t.Name).FirstAsync(ct);
        var done = await db.Suppressions.AnyAsync(s => s.Channel == Channels.Email && s.Address == x.R.Address && s.RemovedAt == null, ct);
        return new UnsubscribeView(company, Mask(x.R.Address), done);
    }

    public async Task<bool> UnsubscribeAsync(string? raw, CancellationToken ct = default)
    {
        if (await ResolveTokenAsync(raw, ct) is not { } x) return false;
        await consent.SuppressAsync(Channels.Email, x.R.Address, SuppressionReasons.Unsubscribe, ct, "marketing");
        consent.Add(x.R.ContactId, Channels.Email, MessagePurposes.Marketing, ConsentStatuses.OptedOut, "unsubscribe_link", $"Unsubscribed from a blast email ({x.R.Address})");
        await db.SaveChangesAsync(ct);
        return true;
    }

    public static string Mask(string address)
    {
        var at = address.IndexOf('@');
        if (at <= 1) return address;
        return address[0] + new string('•', Math.Min(at - 1, 6)) + address[at..];
    }
}
