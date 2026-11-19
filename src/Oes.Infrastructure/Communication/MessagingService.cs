using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Communication;

public sealed record Recipient(Guid ContactId, string ContactName, string Kind, string Address, string Display, bool IsPrimary);

public sealed class EmailInput
{
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public Guid ContactId { get; set; }
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public Guid? TemplateId { get; set; }
}

public sealed class SmsInput
{
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public Guid ContactId { get; set; }
    public string To { get; set; } = "";
    public string Body { get; set; } = "";
    public Guid? TemplateId { get; set; }
}

public sealed record SendResult(Guid MessageId, bool Ok, string? Error);

/// <summary>
/// One-to-one email and texts from records, plus inbound texts (with STOP/START/HELP).
/// Every send checks consent and suppression. Messages are linked to the record and everything above it.
/// </summary>
public sealed class MessagingService(
    OesDbContext db, RecordScope scope, RecordItems items, ICurrentUser user, IClock clock, TenantContext tenant,
    MergeFields merge, ConsentService consent, NotificationService notifications,
    IMailProvider mail, ISmsProvider sms, ILogger<MessagingService> log)
{
    public const string OptOutFooter = "Reply STOP to opt out.";
    private static readonly string[] StopWords = ["STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT", "REVOKE", "OPTOUT"];
    private static readonly string[] StartWords = ["START", "UNSTOP", "YES"];
    private static readonly string[] HelpWords = ["HELP", "INFO"];

    // ---------------- Texting number ----------------

    public Task<string?> PhoneNumberAsync(CancellationToken ct = default) =>
        db.TenantPhoneNumbers.Where(p => p.IsActive).Select(p => p.Number).FirstOrDefaultAsync(ct);

    public async Task SetPhoneNumberAsync(string? number, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SettingsManage);
        var current = await db.TenantPhoneNumbers.ToListAsync(ct);
        if (string.IsNullOrWhiteSpace(number))
        {
            foreach (var p in current) p.IsActive = false;
            await db.SaveChangesAsync(ct);
            return;
        }
        var e164 = ContactValue.Phone(number);
        if (!e164.StartsWith('+') || e164.Length is < 11 or > 16) throw new CrmRuleException("Enter the full texting number, e.g. (402) 555-0100.");
        if (await db.TenantPhoneNumbers.IgnoreQueryFilters([OesDbContext.TenantFilter]).AnyAsync(p => p.Number == e164 && p.TenantId != tenant.TenantId, ct))
            throw new CrmRuleException("That number belongs to another company.");
        foreach (var p in current) p.IsActive = p.Number == e164;
        if (current.All(p => p.Number != e164)) db.TenantPhoneNumbers.Add(new TenantPhoneNumber { Number = e164, Provider = sms.Name });
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Who can be messaged from a record ----------------

    public async Task<List<Recipient>> RecipientsAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(entityType, entityId);
        var contactIds = await ContactsForAsync(entityType, entityId, ct);
        var rows = await (from m in db.ContactMethods where contactIds.Contains(m.ContactId)
                          join c in db.Contacts on m.ContactId equals c.Id
                          where c.ArchivedAt == null && (m.Kind == ContactMethodKinds.Email || m.Kind == ContactMethodKinds.Mobile || m.Kind == ContactMethodKinds.Phone)
                          select new { m.ContactId, c.DisplayName, m.Kind, m.NormalizedValue, m.Value, m.IsPrimary }).ToListAsync(ct);
        return rows.OrderBy(r => contactIds.IndexOf(r.ContactId)).ThenByDescending(r => r.IsPrimary).ThenBy(r => r.Kind == ContactMethodKinds.Mobile ? 0 : 1)
            .Select(r => new Recipient(r.ContactId, r.DisplayName, r.Kind, r.NormalizedValue,
                ContactMethodKinds.IsPhone(r.Kind) ? ContactValue.FormatPhone(r.NormalizedValue, r.Value) : r.Value, r.IsPrimary)).ToList();
    }

    private async Task<List<Guid>> ContactsForAsync(string entityType, Guid entityId, CancellationToken ct)
    {
        switch (entityType)
        {
            case EntityTypes.Contact: return [entityId];
            case EntityTypes.Property:
                return await db.PropertyContacts.Where(p => p.PropertyId == entityId).OrderByDescending(p => p.IsPrimary).Select(p => p.ContactId).ToListAsync(ct);
        }
        // Everything else hangs off an opportunity: message its primary contact.
        var targets = await items.LinkTargetsAsync(entityType, entityId, ct);
        return targets.Where(t => t.Type == EntityTypes.Contact).Select(t => t.Id).Distinct().ToList();
    }

    private async Task<Recipient> ResolveRecipientAsync(string? entityType, Guid? entityId, Guid contactId, string to, string channel, CancellationToken ct)
    {
        List<Recipient> options;
        if (entityType is not null && entityId is { } eid) options = await RecipientsAsync(entityType, eid, ct);
        else options = await RecipientsAsync(EntityTypes.Contact, contactId, ct);
        var kinds = ConsentService.KindsFor(channel);
        var address = channel == Channels.Sms ? ContactValue.Phone(to) : to.Trim().ToLowerInvariant();
        return options.FirstOrDefault(r => r.ContactId == contactId && kinds.Contains(r.Kind) && r.Address == address)
               ?? throw new CrmRuleException(channel == Channels.Sms ? "Pick one of the contact's phone numbers." : "Pick one of the contact's email addresses.");
    }

    private async Task<(MessageTemplate? Template, string Purpose)> TemplateAsync(Guid? id, string channel, CancellationToken ct)
    {
        if (id is null) return (null, MessagePurposes.Transactional);
        var t = await db.MessageTemplates.FirstOrDefaultAsync(x => x.Id == id && x.Channel == channel, ct) ?? throw new RecordNotFoundException("Template");
        return (t, t.Category);
    }

    private async Task LinkAsync(string itemType, Guid itemId, string? entityType, Guid? entityId, Guid contactId, CancellationToken ct)
    {
        if (entityType is not null && entityId is { } eid)
        {
            await items.LinkItemAsync(itemType, itemId, entityType, eid, ct);
            var targets = await items.LinkTargetsAsync(entityType, eid, ct);
            if (targets.Contains((EntityTypes.Contact, contactId))) return;
        }
        await items.LinkItemAsync(itemType, itemId, EntityTypes.Contact, contactId, ct);
    }

    // ---------------- Email ----------------

    public async Task<SendResult> SendEmailAsync(EmailInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.EmailSend);
        var me = user.UserId ?? throw new RecordForbiddenException("Sign in to send email.");
        var to = await ResolveRecipientAsync(input.EntityType, input.EntityId, input.ContactId, input.To, Channels.Email, ct);
        var (template, purpose) = await TemplateAsync(input.TemplateId, Channels.Email, ct);
        if (string.IsNullOrWhiteSpace(input.Subject) || input.Subject.Length > 300) throw new CrmRuleException("Add a subject (up to 300 characters).");
        if (string.IsNullOrWhiteSpace(input.Body) || input.Body.Length > 100_000) throw new CrmRuleException("Write a message.");
        if (await consent.BlockReasonAsync(to.ContactId, Channels.Email, to.Address, purpose, ct) is { } blocked) throw new CrmRuleException($"Not sent: {blocked}");

        var values = await merge.ForAsync(input.EntityType, input.EntityId, to.ContactId, ct);
        var subject = MergeFields.Render(input.Subject.Trim(), values).ReplaceLineEndings(" ");
        var body = MergeFields.Render(input.Body.Trim(), values);
        var from = await db.Users.Where(u => u.Id == me).Select(u => new { u.Email, Name = u.FirstName + " " + u.LastName }).FirstAsync(ct);

        var result = await SafeAsync(() => mail.SendAsync(me, new OutgoingEmail(from.Email ?? "", from.Name, [to.Address], [], subject, ToHtml(body), body), ct));
        var msg = new EmailMessage
        {
            Direction = Directions.Outbound, FromAddress = from.Email ?? "", Subject = subject, Body = body, SentAt = clock.UtcNow, SentByUserId = me,
            Status = result.Ok ? MessageStatuses.Sent : MessageStatuses.Failed, ProviderMessageId = result.ProviderId, Error = Trim(result.Error, 1000),
            TemplateId = template?.Id, EntityType = input.EntityType, EntityId = input.EntityId,
            Recipients = [new EmailRecipient { Kind = "to", Address = to.Address, ContactId = to.ContactId }],
        };
        db.EmailMessages.Add(msg);
        await LinkAsync(ItemTypes.Email, msg.Id, input.EntityType, input.EntityId, to.ContactId, ct);
        await db.SaveChangesAsync(ct);
        return new SendResult(msg.Id, result.Ok, result.Error);
    }

    /// <summary>Plain text to safe HTML: everything encoded, line breaks kept. No user HTML is ever sent.</summary>
    public static string ToHtml(string text) =>
        "<div style=\"font-family:Arial,sans-serif;font-size:14px;line-height:1.5\">" +
        WebUtility.HtmlEncode(text).ReplaceLineEndings("<br>\n") + "</div>";

    // ---------------- SMS ----------------

    public async Task<SendResult> SendSmsAsync(SmsInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SmsSend);
        var me = user.UserId ?? throw new RecordForbiddenException("Sign in to send texts.");
        var from = await PhoneNumberAsync(ct) ?? throw new CrmRuleException("Texting isn't set up yet. An administrator needs to add the company's texting number.");
        var to = await ResolveRecipientAsync(input.EntityType, input.EntityId, input.ContactId, input.To, Channels.Sms, ct);
        var (template, purpose) = await TemplateAsync(input.TemplateId, Channels.Sms, ct);
        if (string.IsNullOrWhiteSpace(input.Body)) throw new CrmRuleException("Write a message.");
        if (await consent.BlockReasonAsync(to.ContactId, Channels.Sms, to.Address, purpose, ct) is { } blocked) throw new CrmRuleException($"Not sent: {blocked}");

        var values = await merge.ForAsync(input.EntityType, input.EntityId, to.ContactId, ct);
        var body = MergeFields.Render(input.Body.Trim(), values);
        // The first text to a number (and every marketing text) says how to opt out.
        var first = !await db.SmsMessages.AnyAsync(m => m.ToNumber == to.Address && m.Direction == Directions.Outbound && m.Status == MessageStatuses.Sent, ct);
        if ((first || purpose == MessagePurposes.Marketing) && !body.Contains("STOP", StringComparison.OrdinalIgnoreCase))
            body = $"{body}\n{OptOutFooter}";
        if (body.Length > TemplateService.SmsMaxLength) throw new CrmRuleException($"Texts can be up to {TemplateService.SmsMaxLength} characters.");

        var result = await SafeAsync(() => sms.SendAsync(from, to.Address, body, ct));
        var msg = new SmsMessage
        {
            Direction = Directions.Outbound, FromNumber = from, ToNumber = to.Address, Body = body, At = clock.UtcNow, SentByUserId = me,
            Status = result.Ok ? MessageStatuses.Sent : MessageStatuses.Failed, ProviderSid = result.ProviderId, Error = Trim(result.Error, 1000),
            ContactId = to.ContactId, TemplateId = template?.Id, EntityType = input.EntityType, EntityId = input.EntityId,
        };
        db.SmsMessages.Add(msg);
        await LinkAsync(ItemTypes.Sms, msg.Id, input.EntityType, input.EntityId, to.ContactId, ct);
        await db.SaveChangesAsync(ct);
        return new SendResult(msg.Id, result.Ok, result.Error);
    }

    /// <summary>
    /// A text from a person to a company number. The tenant comes from the number it was sent to.
    /// Returns false when no company owns that number. Runs without a signed-in user.
    /// </summary>
    public async Task<bool> ReceiveSmsAsync(string fromRaw, string toRaw, string? bodyRaw, string? providerSid, CancellationToken ct = default)
    {
        var to = ContactValue.Phone(toRaw);
        var fromNumber = ContactValue.Phone(fromRaw);
        if (to.Length < 8 || fromNumber.Length < 8) return false;
        var owner = await db.TenantPhoneNumbers.IgnoreQueryFilters([OesDbContext.TenantFilter])
            .Where(p => p.Number == to && p.IsActive).Select(p => (Guid?)p.TenantId).FirstOrDefaultAsync(ct);
        if (owner is not { } tenantId) return false;
        tenant.SetTenant(tenantId);

        if (providerSid is not null && await db.SmsMessages.AnyAsync(m => m.ProviderSid == providerSid && m.Direction == Directions.Inbound, ct))
            return true; // provider retry

        var body = (bodyRaw ?? "").Trim();
        if (body.Length > TemplateService.SmsMaxLength) body = body[..TemplateService.SmsMaxLength];
        var contacts = await (from m in db.ContactMethods
                              where m.NormalizedValue == fromNumber && (m.Kind == ContactMethodKinds.Mobile || m.Kind == ContactMethodKinds.Phone)
                              join c in db.Contacts on m.ContactId equals c.Id
                              where c.ArchivedAt == null
                              select new { c.Id, c.OwnerUserId, c.DisplayName }).Distinct().ToListAsync(ct);
        // Continue the conversation on the record the last text to this number came from.
        var last = await db.SmsMessages.Where(m => m.ToNumber == fromNumber && m.Direction == Directions.Outbound)
            .OrderByDescending(m => m.At).Select(m => new { m.EntityType, m.EntityId, m.ContactId, m.SentByUserId }).FirstOrDefaultAsync(ct);
        var contactId = last?.ContactId is { } lc && contacts.Any(c => c.Id == lc) ? lc : contacts.FirstOrDefault()?.Id;

        var inbound = new SmsMessage
        {
            Direction = Directions.Inbound, FromNumber = fromNumber, ToNumber = to, Body = body, At = clock.UtcNow,
            Status = MessageStatuses.Received, ProviderSid = providerSid, ContactId = contactId,
            EntityType = contactId == last?.ContactId ? last?.EntityType : null, EntityId = contactId == last?.ContactId ? last?.EntityId : null,
        };
        db.SmsMessages.Add(inbound);
        if (contactId is { } cid)
        {
            await LinkAsync(ItemTypes.Sms, inbound.Id, inbound.EntityType, inbound.EntityId, cid, ct);
            foreach (var other in contacts.Where(c => c.Id != cid)) await items.LinkItemAsync(ItemTypes.Sms, inbound.Id, EntityTypes.Contact, other.Id, ct);
        }

        var company = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).FirstAsync(ct);
        var keyword = body.ToUpperInvariant().Trim(' ', '.', '!');
        string? reply = null;
        if (StopWords.Contains(keyword))
        {
            await consent.SuppressAsync(Channels.Sms, fromNumber, SuppressionReasons.Stop, ct);
            foreach (var c in contacts)
                foreach (var p in MessagePurposes.All) consent.Add(c.Id, Channels.Sms, p, ConsentStatuses.OptedOut, "keyword", $"Texted \"{keyword}\" to {to}");
            reply = $"{company}: You're unsubscribed and won't get more texts from us. Reply START to resubscribe.";
        }
        else if (StartWords.Contains(keyword))
        {
            await consent.UnsuppressAsync(Channels.Sms, fromNumber, ct);
            foreach (var c in contacts) consent.Add(c.Id, Channels.Sms, MessagePurposes.Transactional, ConsentStatuses.OptedIn, "keyword", $"Texted \"{keyword}\" to {to}");
            reply = $"{company}: You're resubscribed to texts about your project. Reply STOP to opt out.";
        }
        else if (HelpWords.Contains(keyword))
        {
            reply = $"{company}: Texts about your project. Msg & data rates may apply. Reply STOP to opt out.";
        }
        else
        {
            var who = last?.SentByUserId ?? contacts.Select(c => c.OwnerUserId).FirstOrDefault(o => o is not null);
            var name = contacts.FirstOrDefault(c => c.Id == contactId)?.DisplayName ?? ContactValue.FormatPhone(fromNumber, fromNumber);
            var url = contactId is { } id2 ? RecordUrls.For(inbound.EntityType ?? EntityTypes.Contact, inbound.EntityId ?? id2) : null;
            if (contactId is null)
                await notifications.NotifyPermissionAsync(Permissions.SmsViewAll, "sms.unmatched", $"Text from an unknown number, {name}", Trim(body, 200), "/Messages/Unmatched", ct);
            else
                notifications.Notify(who, "sms.received", $"Text from {name}", Trim(body, 200), url);
        }
        await db.SaveChangesAsync(ct);

        if (reply is not null)
        {
            // Compliance replies go out even to a suppressed number: they confirm the opt-out.
            var r = await SafeAsync(() => sms.SendAsync(to, fromNumber, reply, ct));
            var auto = new SmsMessage
            {
                Direction = Directions.Outbound, FromNumber = to, ToNumber = fromNumber, Body = reply, At = clock.UtcNow,
                Status = r.Ok ? MessageStatuses.Sent : MessageStatuses.Failed, ProviderSid = r.ProviderId, Error = Trim(r.Error, 1000), ContactId = contactId,
            };
            db.SmsMessages.Add(auto);
            foreach (var c in contacts) await items.LinkItemAsync(ItemTypes.Sms, auto.Id, EntityTypes.Contact, c.Id, ct);
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    // ---------------- Reading ----------------

    public sealed record MessageView(string Channel, Guid Id, string Direction, DateTime At, string? SenderName, string Counterparty,
        string? Subject, string Body, string Status, string? Error);

    public async Task<MessageView> GetAsync(string channel, Guid id, CancellationToken ct = default)
    {
        var links = await db.RecordLinks.Where(l => l.ItemType == channel && l.ItemId == id && l.IsPrimary).Select(l => new { l.EntityType, l.EntityId }).ToListAsync(ct);
        if (links.Count == 0) links = await db.RecordLinks.Where(l => l.ItemType == channel && l.ItemId == id).Select(l => new { l.EntityType, l.EntityId }).ToListAsync(ct);
        var visible = false;
        foreach (var l in links) if (await scope.CanSeeAsync(l.EntityType, l.EntityId)) { visible = true; break; }
        if (!visible) throw new RecordNotFoundException("Message");

        if (channel == ItemTypes.Email)
        {
            var m = await db.EmailMessages.Include(x => x.Recipients).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Message");
            await EnsureMayReadAsync(m.SentByUserId, m.Direction, Permissions.EmailViewAll, ct);
            var sender = await NameAsync(m.SentByUserId, ct);
            return new MessageView(channel, m.Id, m.Direction, m.SentAt, sender, string.Join(", ", m.Recipients.Select(r => r.Address)), m.Subject, m.Body, m.Status, m.Error);
        }
        var s = await db.SmsMessages.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Message");
        await EnsureMayReadAsync(s.SentByUserId, s.Direction, Permissions.SmsViewAll, ct);
        var other = s.Direction == Directions.Inbound ? s.FromNumber : s.ToNumber;
        return new MessageView(channel, s.Id, s.Direction, s.At, await NameAsync(s.SentByUserId, ct), ContactValue.FormatPhone(other, other), null, s.Body, s.Status, s.Error);
    }

    private async Task EnsureMayReadAsync(Guid? sentBy, string direction, string viewAll, CancellationToken ct)
    {
        if (direction == Directions.Inbound || sentBy is null || sentBy == user.UserId) return;
        if (!await user.HasPermissionAsync(viewAll, ct)) throw new RecordNotFoundException("Message");
    }

    private async Task<string?> NameAsync(Guid? id, CancellationToken ct) =>
        id is null ? null : await db.Users.Where(u => u.Id == id).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);

    private async Task<ProviderResult> SafeAsync(Func<Task<ProviderResult>> send)
    {
        try { return await send(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Message provider failed");
            return new ProviderResult(false, null, "The message couldn't be sent. Try again in a minute.");
        }
    }

    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
