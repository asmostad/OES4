using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
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
            // A person's own STOP/unsubscribe covers its scope, and only they can undo it.
            if (await db.Suppressions.AnyAsync(s => s.RemovedAt == null && s.Channel == channel && addresses.Contains(s.Address)
                    && PersonOnlyReasons.Contains(s.Reason) && (s.Scope == "all" || purpose == MessagePurposes.Marketing), ct))
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

    /// <summary>Adds a suppression unless one already covers it. Saved with the caller's SaveChanges.</summary>
    internal async Task SuppressAsync(string channel, string address, string reason, CancellationToken ct, string suppressionScope = "all")
    {
        var covered = await db.Suppressions.AnyAsync(s => s.Channel == channel && s.Address == address && s.RemovedAt == null
                                                          && (s.Scope == "all" || s.Scope == suppressionScope), ct);
        if (!covered) db.Suppressions.Add(new Suppression { Channel = channel, Address = address, Scope = suppressionScope, Reason = reason });
    }

    internal async Task UnsuppressAsync(string channel, string address, CancellationToken ct)
    {
        foreach (var s in await db.Suppressions.Where(s => s.Channel == channel && s.Address == address && s.RemovedAt == null).ToListAsync(ct))
            s.RemovedAt = clock.UtcNow;
    }

    // ---------------- Suppression list (admin) ----------------

    public async Task<List<SuppressionRow>> SuppressionsAsync(string channel, string? q, int take = 500, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BlastsManage);
        var query = db.Suppressions.Where(s => s.Channel == channel && s.RemovedAt == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLowerInvariant();
            query = query.Where(s => s.Address.Contains(t));
        }
        return await query.OrderByDescending(s => s.CreatedAt).Take(take)
            .Select(s => new SuppressionRow(s.Id, s.Channel, s.Address, s.Scope, s.Reason, s.CreatedAt)).ToListAsync(ct);
    }

    public async Task AddManualSuppressionAsync(string channel, string address, string suppressionScope, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BlastsManage);
        if (!Channels.All.Contains(channel)) throw new CrmRuleException("Pick email or text.");
        if (suppressionScope is not ("all" or "marketing")) throw new CrmRuleException("Pick what to block.");
        var normalized = channel == Channels.Sms ? ContactValue.Phone(address ?? "") : (address ?? "").Trim().ToLowerInvariant();
        if (channel == Channels.Email && (normalized.Length is < 3 or > 320 || !normalized.Contains('@'))) throw new CrmRuleException("Enter an email address.");
        if (channel == Channels.Sms && normalized.Length < 11) throw new CrmRuleException("Enter a full phone number.");
        await SuppressAsync(channel, normalized, SuppressionReasons.Manual, ct, suppressionScope);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Staff can lift manual and bounce blocks, never a person's own unsubscribe, STOP or complaint.</summary>
    public async Task RemoveSuppressionAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.BlastsManage);
        var s = await db.Suppressions.FirstOrDefaultAsync(x => x.Id == id && x.RemovedAt == null, ct) ?? throw new RecordNotFoundException("Suppression");
        if (PersonOnlyReasons.Contains(s.Reason)) throw new CrmRuleException("Only the person can undo their own opt-out.");
        s.RemovedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Can we message them? ----------------

    /// <summary>Null when the message may be sent; otherwise why it's blocked.</summary>
    public async Task<string?> BlockReasonAsync(Guid contactId, string channel, string address, string purpose, CancellationToken ct = default) =>
        (await BlockReasonsAsync([(contactId, address)], channel, purpose, ct)).GetValueOrDefault((contactId, address));

    /// <summary>Block reasons for many (contact, address) pairs at once. Pairs that may be messaged are absent.</summary>
    public async Task<Dictionary<(Guid ContactId, string Address), string>> BlockReasonsAsync(
        IReadOnlyCollection<(Guid ContactId, string Address)> targets, string channel, string purpose, CancellationToken ct = default)
    {
        var result = new Dictionary<(Guid, string), string>();
        if (targets.Count == 0) return result;
        var contactIds = targets.Select(t => t.ContactId).Distinct().ToList();
        var addresses = targets.Select(t => t.Address).Distinct().ToList();

        var suppressed = new List<(string Address, string Scope, string Reason)>();
        foreach (var chunk in addresses.Chunk(500))
        {
            var rows = await db.Suppressions.Where(s => s.Channel == channel && s.RemovedAt == null && chunk.Contains(s.Address))
                .Select(s => new { s.Address, s.Scope, s.Reason }).ToListAsync(ct);
            suppressed.AddRange(rows.Select(s => (s.Address, s.Scope, s.Reason)));
        }
        var consents = new List<(Guid ContactId, string Purpose, string Status, DateTime At)>();
        foreach (var chunk in contactIds.Chunk(500))
        {
            var rows = await db.ContactConsents.Where(c => c.Channel == channel && chunk.Contains(c.ContactId))
                .Select(c => new { c.ContactId, c.Purpose, c.Status, c.At }).ToListAsync(ct);
            consents.AddRange(rows.Select(c => (c.ContactId, c.Purpose, c.Status, c.At)));
        }
        var latest = consents.GroupBy(c => (c.ContactId, c.Purpose)).ToDictionary(g => g.Key, g => g.MaxBy(c => c.At).Status);

        foreach (var (contactId, address) in targets)
        {
            var hit = suppressed.FirstOrDefault(s => s.Address == address && (s.Scope == "all" || purpose == MessagePurposes.Marketing));
            if (hit.Address is not null)
            {
                result[(contactId, address)] = hit.Reason switch
                {
                    SuppressionReasons.Stop => "This number texted STOP.",
                    SuppressionReasons.Unsubscribe => "They unsubscribed.",
                    _ => $"This address is suppressed ({hit.Reason}).",
                };
                continue;
            }
            var transactional = latest.GetValueOrDefault((contactId, MessagePurposes.Transactional), ConsentStatuses.Unknown);
            var marketing = latest.GetValueOrDefault((contactId, MessagePurposes.Marketing), ConsentStatuses.Unknown);
            // Opting out of everyday messages on a channel also opts out of marketing on it.
            if (transactional == ConsentStatuses.OptedOut) { result[(contactId, address)] = "The contact opted out."; continue; }
            if (purpose == MessagePurposes.Marketing && marketing == ConsentStatuses.OptedOut) { result[(contactId, address)] = "The contact opted out of marketing."; continue; }
            if (channel == Channels.Sms)
            {
                var ok = purpose == MessagePurposes.Marketing
                    ? marketing == ConsentStatuses.OptedIn
                    : transactional == ConsentStatuses.OptedIn || marketing == ConsentStatuses.OptedIn;
                if (!ok) result[(contactId, address)] = purpose == MessagePurposes.Marketing
                    ? "No marketing text opt-in on file."
                    : "No texting opt-in on file. Record their consent on the contact first.";
            }
        }
        return result;
    }
}
