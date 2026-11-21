using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Ai;
using Oes.Web.Infrastructure;

namespace Oes.Web.Controllers.Api;

public sealed record AssistantHome(bool Configured, List<ConversationItem> Conversations);
public sealed record ConversationItem(Guid Id, string Title, DateTime LastMessageAt);
public sealed record ChatLineDto(string Role, string Text, DateTime At, List<string> Tools);
public sealed record ConversationDto(Guid Id, string Title, string? EntityType, Guid? EntityId, string? RecordLabel, string? RecordUrl, List<ChatLineDto> Lines);
public sealed record StartConversationInput(string? EntityType, Guid? EntityId);
public sealed record ChatInput(string Text);
public sealed record StartedDto(Guid Id);

/// <summary>
/// The assistant from the field app, for people with ai.assistant. Same tools and visibility as the web (it looks things
/// up as the user). Replies come back whole rather than streamed. Sending is idempotent, so a retry on a bad
/// connection never asks (or bills) twice.
/// </summary>
[ApiController]
[Route("api/v1/assistant")]
[HasPermission(Permissions.AiAssistant)]
public sealed class AssistantApiController(AssistantService assistant) : ControllerBase
{
    [HttpGet]
    public async Task<AssistantHome> Home(CancellationToken ct) =>
        new(assistant.IsConfigured, (await assistant.ListAsync(ct)).Select(c => new ConversationItem(c.Id, c.Title, c.LastMessageAt)).ToList());

    /// <summary>A new conversation, optionally about a record ("Ask about this lead").</summary>
    [HttpPost("conversations")]
    [Idempotent]
    public async Task<StartedDto> Start(StartConversationInput input, CancellationToken ct) => new(await assistant.StartAsync(input.EntityType, input.EntityId, ct));

    [HttpGet("conversations/{id:guid}")]
    public async Task<ConversationDto> Get(Guid id, CancellationToken ct) => Map(await assistant.GetAsync(id, ct));

    /// <summary>Sends a message and waits for the answer; returns the whole conversation with it.</summary>
    [HttpPost("conversations/{id:guid}/messages")]
    [Idempotent]
    public async Task<ConversationDto> Send(Guid id, ChatInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Text)) throw new Oes.Infrastructure.Crm.CrmRuleException("Type a message.");
        await assistant.SendAsync(id, input.Text, ct);
        return Map(await assistant.GetAsync(id, ct));
    }

    [HttpDelete("conversations/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await assistant.DeleteAsync(id, ct);
        return NoContent();
    }

    private static ConversationDto Map(ConversationView v) =>
        new(v.Conversation.Id, v.Conversation.Title, v.Conversation.EntityType, v.Conversation.EntityId, v.RecordLabel, v.RecordUrl,
            v.Lines.Select(l => new ChatLineDto(l.Role, l.Text, l.At, l.Tools.Select(AssistantTools.Label).ToList())).ToList());
}
