using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Controllers.Api;

public sealed record UnmatchedText(Guid Id, string Direction, string Body, DateTime At, string Status);
public sealed record UnmatchedThreadDto(string Number, string Display, int Unread, DateTime LastAt, List<UnmatchedText> Messages, List<LinkOption> Possible);
public sealed record LinkOption(Guid Id, string Name);
public sealed record NumberInput(string Number);
public sealed record LinkInput(string Number, Guid ContactId);
public sealed record NumberLeadInput(string Number, string? FirstName, string? LastName, bool CreateOpportunity);
public sealed record NumberReplyInput(string Number, string Body);
public sealed record NewFromNumber(Guid ContactId, Guid? OpportunityId);

/// <summary>
/// Texts from numbers that aren't on any contact, for people with sms.view_all: reply, file them on a contact (the
/// number is added to them), start a lead, or dismiss. Same rules as the web inbox.
/// </summary>
[ApiController]
[Route("api/v1/texts/unmatched")]
[HasPermission(Permissions.SmsViewAll)]
public sealed class UnmatchedTextsApiController(UnmatchedTextService texts, MessagingService messaging) : ControllerBase
{
    [HttpGet]
    public async Task<List<UnmatchedThreadDto>> List(CancellationToken ct) =>
        (await texts.ListAsync(ct)).Select(t => new UnmatchedThreadDto(t.Number, t.Display, t.Unread, t.LastAt,
            t.Messages.Select(m => new UnmatchedText(m.Id, m.Direction, m.Body, m.At, m.Status)).ToList(),
            t.Possible.Select(p => new LinkOption(p.Id, p.Name)).ToList())).ToList();

    [HttpPost("link")]
    [Idempotent]
    public async Task<IActionResult> Link(LinkInput input, CancellationToken ct)
    {
        await texts.LinkAsync(input.Number, input.ContactId, ct);
        return NoContent();
    }

    [HttpPost("lead")]
    [Idempotent]
    public async Task<NewFromNumber> Lead(NumberLeadInput input, CancellationToken ct)
    {
        var (contactId, oppId) = await texts.NewLeadAsync(input.Number, input.FirstName, input.LastName, input.CreateOpportunity, ct);
        return new NewFromNumber(contactId, oppId);
    }

    /// <summary>Replies from the company number (needs sms.send too). A provider failure comes back as ok = false.</summary>
    [HttpPost("reply")]
    [Idempotent]
    public async Task<SentDto> Reply(NumberReplyInput input, CancellationToken ct)
    {
        var r = await messaging.ReplyToUnknownAsync(input.Number, input.Body, ct);
        return new SentDto(r.MessageId, r.Ok, r.Error);
    }

    [HttpPost("dismiss")]
    [Idempotent]
    public async Task<IActionResult> Dismiss(NumberInput input, CancellationToken ct)
    {
        await texts.DismissAsync(input.Number, ct);
        return NoContent();
    }
}
