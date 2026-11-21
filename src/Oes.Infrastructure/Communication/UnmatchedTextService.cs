using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Communication;

public sealed record UnmatchedThread(string Number, string Display, int Unread, DateTime LastAt, List<SmsMessage> Messages,
    List<(Guid Id, string Name)> Possible);

/// <summary>
/// Texts from numbers that aren't on any contact. Kept (never dropped) and shown to people with
/// sms.view_all, who link the number to a contact, start a new lead from it, or dismiss it.
/// Linking adds the number to the contact, so the next text matches by itself.
/// </summary>
public sealed class UnmatchedTextService(OesDbContext db, RecordScope scope, RecordItems items, ContactService contacts,
    OpportunityService opportunities, IClock clock)
{
    private IQueryable<SmsMessage> Open => db.SmsMessages.Where(m => m.Direction == Directions.Inbound && m.ContactId == null && m.ReviewedAt == null);

    public async Task<int> CountAsync(CancellationToken ct = default) =>
        await Open.Select(m => m.FromNumber).Distinct().CountAsync(ct);

    public async Task<List<UnmatchedThread>> ListAsync(CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SmsViewAll);
        var numbers = await Open.GroupBy(m => m.FromNumber).Select(g => new { Number = g.Key, Count = g.Count(), Last = g.Max(m => m.At) })
            .OrderByDescending(x => x.Last).Take(100).ToListAsync(ct);
        var list = numbers.Select(n => n.Number).ToList();
        // The whole conversation with each number that isn't on a contact: their texts and any auto-replies.
        var messages = await db.SmsMessages.Where(m => m.ContactId == null && (list.Contains(m.FromNumber) || list.Contains(m.ToNumber)))
            .OrderBy(m => m.At).ToListAsync(ct);
        var threads = new List<UnmatchedThread>();
        foreach (var n in numbers)
        {
            // Archived contacts with the number are offered as likely matches.
            var possible = await (from m in db.ContactMethods where m.NormalizedValue == n.Number
                                  join c in (await scope.ContactsAsync(includeArchived: true)) on m.ContactId equals c.Id
                                  select new { c.Id, c.DisplayName }).Distinct().Take(5).ToListAsync(ct);
            threads.Add(new UnmatchedThread(n.Number, ContactValue.FormatPhone(n.Number, n.Number), n.Count, n.Last,
                messages.Where(m => m.FromNumber == n.Number || m.ToNumber == n.Number).TakeLast(20).ToList(),
                possible.Select(p => (p.Id, p.DisplayName)).ToList()));
        }
        return threads;
    }

    /// <summary>Adds the number to the contact (as a mobile, unless they already have it) and files the texts on them.</summary>
    public async Task LinkAsync(string number, Guid contactId, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SmsViewAll);
        var contact = await (await scope.ContactsAsync()).FirstOrDefaultAsync(c => c.Id == contactId, ct) ?? throw new RecordNotFoundException("Contact");
        await scope.EnsureCanEditAsync(contact.OwnerUserId);
        number = ContactValue.Phone(number);
        if (!await db.ContactMethods.AnyAsync(m => m.ContactId == contactId && m.NormalizedValue == number, ct))
            db.ContactMethods.Add(new ContactMethod
            {
                ContactId = contactId, Kind = ContactMethodKinds.Mobile, Value = ContactValue.FormatPhone(number, number), NormalizedValue = number,
                IsPrimary = !await db.ContactMethods.AnyAsync(m => m.ContactId == contactId && m.Kind == ContactMethodKinds.Mobile && m.IsPrimary, ct),
            });
        var n = await FileAsync(number, contactId, null, null, ct);
        await items.LogAsync(EntityTypes.Contact, contactId, "sms.linked", $"{n} text{(n == 1 ? "" : "s")} from {ContactValue.FormatPhone(number, number)} filed here", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A new contact with the number (and, if asked, a lead) for someone who texted in first.</summary>
    public async Task<(Guid ContactId, Guid? OpportunityId)> NewLeadAsync(string number, string? firstName, string? lastName, bool createOpportunity,
        CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SmsViewAll);
        number = ContactValue.Phone(number);
        if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)) throw new CrmRuleException("Enter their name.");
        var contactId = await contacts.CreateAsync(new ContactInput
        {
            FirstName = firstName, LastName = lastName, SourceKey = "phone_in", OwnerUserId = scope.Me,
            Methods = [new(ContactMethodKinds.Mobile, ContactValue.FormatPhone(number, number), null, true)],
        }, ct);
        Guid? oppId = createOpportunity
            ? await opportunities.CreateAsync(new OpportunityInput { PrimaryContactId = contactId, SourceKey = "phone_in", OwnerUserId = scope.Me }, ct)
            : null;
        await FileAsync(number, contactId, oppId is null ? null : EntityTypes.Opportunity, oppId, ct);
        await db.SaveChangesAsync(ct);
        return (contactId, oppId);
    }

    /// <summary>Takes the number out of the inbox (spam, a wrong number). A new text from it brings it back.</summary>
    public async Task DismissAsync(string number, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SmsViewAll);
        number = ContactValue.Phone(number);
        var now = clock.UtcNow;
        await Open.Where(m => m.FromNumber == number).ExecuteUpdateAsync(s => s.SetProperty(m => m.ReviewedAt, now), ct);
    }

    private async Task<int> FileAsync(string number, Guid contactId, string? entityType, Guid? entityId, CancellationToken ct)
    {
        var messages = await db.SmsMessages.Where(m => m.ContactId == null && (m.FromNumber == number || m.ToNumber == number)).ToListAsync(ct);
        foreach (var m in messages)
        {
            m.ContactId = contactId;
            m.EntityType ??= entityType;
            m.EntityId ??= entityId;
            if (entityType is not null && entityId is { } eid) await items.LinkItemAsync(ItemTypes.Sms, m.Id, entityType, eid, ct);
            else await items.LinkItemAsync(ItemTypes.Sms, m.Id, EntityTypes.Contact, contactId, ct);
        }
        return messages.Count(m => m.Direction == Directions.Inbound);
    }
}
