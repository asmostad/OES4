using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;

namespace Oes.Web.ViewComponents;

public sealed class ComposeModel
{
    public required string EntityType { get; init; }
    public required Guid EntityId { get; init; }
    public required List<Recipient> Emails { get; init; }
    public required List<(Recipient To, string? Blocked)> Phones { get; init; }
    public required List<MessageTemplate> EmailTemplates { get; init; }
    public required List<MessageTemplate> SmsTemplates { get; init; }
    public bool CanEmail { get; init; }
    public bool CanSms { get; init; }
    public bool TextingReady { get; init; }
    /// <summary>Files on the record (and under it) the email can attach.</summary>
    public List<Oes.Infrastructure.Crm.RecordItems.AttachableDocument> Files { get; init; } = [];
}

/// <summary>Email / text compose card for a record: <c>@await Component.InvokeAsync("RecordMessaging", new { entityType, entityId })</c>.</summary>
public sealed class RecordMessagingViewComponent(MessagingService messaging, TemplateService templates, ConsentService consent, ICurrentUser user,
    Oes.Infrastructure.Crm.RecordItems items) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string entityType, Guid entityId)
    {
        var canEmail = await user.HasPermissionAsync(Permissions.EmailSend);
        var canSms = await user.HasPermissionAsync(Permissions.SmsSend);
        if (!canEmail && !canSms) return Content("");
        var recipients = await messaging.RecipientsAsync(entityType, entityId);
        var phones = new List<(Recipient, string?)>();
        foreach (var r in recipients.Where(r => ContactMethodKinds.IsPhone(r.Kind)))
            phones.Add((r, await consent.BlockReasonAsync(r.ContactId, Channels.Sms, r.Address, MessagePurposes.Transactional)));
        return View("/Pages/Shared/_RecordMessaging.cshtml", new ComposeModel
        {
            EntityType = entityType, EntityId = entityId,
            Emails = recipients.Where(r => r.Kind == ContactMethodKinds.Email).ToList(), Phones = phones,
            EmailTemplates = await templates.ListAsync(Channels.Email, activeOnly: true),
            SmsTemplates = await templates.ListAsync(Channels.Sms, activeOnly: true),
            CanEmail = canEmail, CanSms = canSms, TextingReady = await messaging.PhoneNumberAsync() is not null,
            Files = canEmail ? await items.AttachableDocumentsAsync(entityType, entityId) : [],
        });
    }
}
