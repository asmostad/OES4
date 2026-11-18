using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Communication;

public sealed record ConsentState(string Channel, string Purpose, string Status, string? Source, DateTime? At, string? RecordedBy);
public sealed record SuppressionRow(Guid Id, string Channel, string Address, string Scope, string Reason, DateTime At);

/// <summary>
/// Consent per contact (a history; the latest row wins) and suppression per address. Rules:
/// texts need an opt-in; email is allowed unless opted out; a suppressed address is never messaged;
/// a STOP or unsubscribe can only be undone by the person (texting START), never by staff.
/// </summary>
public sealed class ConsentService(OesDbContext db, RecordScope scope, IClock clock)
{
    private static readonly string[] PersonOnlyReasons = [SuppressionReasons.Stop, SuppressionReasons.Unsubscribe, SuppressionReasons.Complaint];

    public static string[] KindsFor(string channel) =>
        channel == Channels.Sms ? [ContactMethodKinds.Mobile, ContactMethodKinds.Phone] : [ContactMethodKinds.Email];

    public async Task<List<ConsentState>> CurrentAsync(Guid contactId, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(EntityTypes.Contact, contactId);
        var rows = await (from c in db.ContactConsents where c.ContactId == contactId
                          join u in db.Users on c.RecordedByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                          orderby c.At descending
                          select new { c.Channel, c.Purpose, c.Status, c.Source, c.At, By = u == null ? null : u.FirstName + " " + u.LastName }).ToListAsync(ct);
        return (from ch in Channels.All from p in MessagePurposes.All
                let r = rows.FirstOrDefault(x => x.Channel == ch && x.Purpose == p)
                select new ConsentState(ch, p, r?.Status ?? ConsentStatuses.Unknown, r?.Source, r?.At, r?.By)).ToList();
    }

    public async Task<List<SuppressionRow>> SuppressionsForContactAsync(Guid contactId, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(EntityTypes.Contact, contactId);
        var addresses = await db.ContactMethods.Where(m => m.ContactId == contactId).Select(m => m.NormalizedValue).ToListAsync(ct);
        return await db.Suppressions.Where(s => s.RemovedAt == null && addresses.Contains(s.Address))
            .Select(s => new SuppressionRow(s.Id, s.Channel, s.Address, s.Scope, s.Reason, s.CreatedAt)).ToListAsync(ct);
    }

    public async Task RecordAsync(Guid contactId, string channel, string purpose, string status, string source, string? evidence, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(EntityTypes.Contact, contactId);
        if (!Channels.All.Contains(channel) || !MessagePurposes.All.Contains(purpose)) throw new CrmRuleException("Pick a channel and purpose.");
        if (status is not (ConsentStatuses.OptedIn or ConsentStatuses.OptedOut)) throw new CrmRuleException("Pick opted in or opted out.");
        if (string.IsNullOrWhiteSpace(source) || source.Trim().Length > 30) throw new CrmRuleException("Say how you got their consent.");
        if (status == ConsentStatuses.OptedIn)
        {
            var kinds = KindsFor(channel);
            var addresses = await db.ContactMethods.Where(m => m.ContactId == contactId && kinds.Contains(m.Kind)).Select(m => m.NormalizedValue).ToListAsync(ct);
            if (await db.Suppressions.AnyAsync(s => s.RemovedAt == null && s.Channel == channel && addresses.Contains(s.Address) && PersonOnlyReasons.Contains(s.Reason), ct))
                throw new CrmRuleException(channel == Channels.Sms
                    ? "This contact texted STOP. Only they can opt back in, by texting START."
                    : "This contact unsubscribed. Only they can opt back in.");
        }
        Add(contactId, channel, purpose, status, source.Trim(), evidence);
        await db.SaveChangesAsync(ct);
    }

    internal void Add(Guid contactId, string channel, string purpose, string status, string source, string? evidence)
    {
        var e = string.IsNullOrWhiteSpace(evidence) ? null : evidence.Trim();
        db.ContactConsents.Add(new ContactConsent
        {
            ContactId = contactId, Channel = channel, Purpose = purpose, Status = status, Source = source,
            Evidence = e is null ? null : e[..Math.Min(e.Length, 1000)], RecordedByUserId = scope.Me, At = clock.UtcNow,
        });
    }

    internal async Task SuppressAsync(string channel, string address, string reason, CancellationToken ct)
    {
        if (!await db.Suppressions.AnyAsync(s => s.Channel == channel && s.Address == address && s.RemovedAt == null && s.Scope == "all", ct))
            db.Suppressions.Add(new Suppression { Channel = channel, Address = address, Scope = "all", Reason = reason });
    }

    internal async Task UnsuppressAsync(string channel, string address, CancellationToken ct)
    {
        foreach (var s in await db.Suppressions.Where(s => s.Channel == channel && s.Address == address && s.RemovedAt == null).ToListAsync(ct))
            s.RemovedAt = clock.UtcNow;
    }

    /// <summary>Null when the message may be sent; otherwise why it's blocked.</summary>
    public async Task<string?> BlockReasonAsync(Guid contactId, string channel, string address, string purpose, CancellationToken ct = default)
    {
        var suppressed = await db.Suppressions.Where(s => s.Channel == channel && s.Address == address && s.RemovedAt == null)
            .Select(s => new { s.Scope, s.Reason }).ToListAsync(ct);
        if (suppressed.FirstOrDefault(s => s.Scope == "all" || purpose == MessagePurposes.Marketing) is { } hit)
            return hit.Reason == SuppressionReasons.Stop ? "This number texted STOP." : $"This address is suppressed ({hit.Reason}).";

        var latest = await db.ContactConsents.Where(c => c.ContactId == contactId && c.Channel == channel)
            .OrderByDescending(c => c.At).Select(c => new { c.Purpose, c.Status }).ToListAsync(ct);
        string Status(string p) => latest.FirstOrDefault(c => c.Purpose == p)?.Status ?? ConsentStatuses.Unknown;
        var transactional = Status(MessagePurposes.Transactional);
        var marketing = Status(MessagePurposes.Marketing);

        // Opting out of everyday messages on a channel also opts out of marketing on it.
        if (transactional == ConsentStatuses.OptedOut) return "The contact opted out.";
        if (purpose == MessagePurposes.Marketing && marketing == ConsentStatuses.OptedOut) return "The contact opted out of marketing.";
        if (channel == Channels.Sms)
        {
            var ok = purpose == MessagePurposes.Marketing
                ? marketing == ConsentStatuses.OptedIn
                : transactional == ConsentStatuses.OptedIn || marketing == ConsentStatuses.OptedIn;
            if (!ok) return purpose == MessagePurposes.Marketing
                ? "No marketing text opt-in on file."
                : "No texting opt-in on file. Record their consent on the contact first.";
        }
        return null;
    }
}
