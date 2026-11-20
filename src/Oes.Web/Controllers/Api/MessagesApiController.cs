using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Controllers.Api;

public sealed record RecipientDto(Guid ContactId, string ContactName, string Kind, string Address, string Display, bool IsPrimary);
public sealed record TemplateDto(Guid Id, string Name, string? Subject, string Body, string Category);
public sealed record SentDto(Guid MessageId, bool Ok, string? Error);
public sealed record AttachmentDto(Guid DocumentId, string FileName);
public sealed record MessageDto(string Channel, Guid Id, string Direction, DateTime At, string? SenderName, string Counterparty, string? Subject,
    string Body, string Status, string? Error, string? Cc, List<AttachmentDto> Attachments);

/// <summary>
/// Texts and email from the field app, sent from the company's number and mailbox (not the phone's own apps), so they
/// land on the record's timeline. Same permissions, consent checks, opt-out wording and merge fields as the web.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class MessagesApiController(MessagingService messaging, TemplateService templates) : ControllerBase
{
    /// <summary>Who a message from this record can go to: each contact's mobile, phone and email.</summary>
    [HttpGet("records/{entityType}/{id:guid}/recipients")]
    public async Task<ActionResult<List<RecipientDto>>> Recipients(string entityType, Guid id, CancellationToken ct)
    {
        if (!EntityTypes.All.Contains(entityType)) return NotFound();
        return (await messaging.RecipientsAsync(entityType, id, ct))
            .Select(r => new RecipientDto(r.ContactId, r.ContactName, r.Kind, r.Address, r.Display, r.IsPrimary)).ToList();
    }

    [HttpGet("messages/templates")]
    public async Task<List<TemplateDto>> Templates(string channel, CancellationToken ct) =>
        (await templates.ListAsync(channel == Channels.Sms ? Channels.Sms : Channels.Email, activeOnly: true, ct))
            .Select(t => new TemplateDto(t.Id, t.Name, t.Subject, t.Body, t.Category)).ToList();

    /// <summary>Sends a text. A provider failure still records the message (status failed) and comes back with ok = false.</summary>
    [HttpPost("messages/sms")]
    [Idempotent]
    public async Task<SentDto> Sms(SmsInput input, CancellationToken ct) => Sent(await messaging.SendSmsAsync(input, ct));

    [HttpPost("messages/email")]
    [Idempotent]
    public async Task<SentDto> Email(EmailInput input, CancellationToken ct) => Sent(await messaging.SendEmailAsync(input, ct));

    /// <summary>One message in full (a timeline row only shows the start).</summary>
    [HttpGet("messages/{channel}/{id:guid}")]
    public async Task<ActionResult<MessageDto>> Get(string channel, Guid id, CancellationToken ct)
    {
        if (channel is not (ItemTypes.Email or ItemTypes.Sms)) return NotFound();
        var m = await messaging.GetAsync(channel, id, ct);
        return new MessageDto(m.Channel, m.Id, m.Direction, m.At, m.SenderName, m.Counterparty, m.Subject, m.Body, m.Status, m.Error, m.Cc,
            m.Attachments.Select(a => new AttachmentDto(a.DocumentId, a.FileName)).ToList());
    }

    private static SentDto Sent(SendResult r) => new(r.MessageId, r.Ok, r.Error);
}
