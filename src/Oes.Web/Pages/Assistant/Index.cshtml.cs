using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Assistant;

/// <summary>
/// Chat with the assistant. With script on, the answer streams in as it's written (Stream handler, server-sent
/// events); without it, the form posts (Send) and the page reloads with the reply.
/// </summary>
[HasPermission(Permissions.AiAssistant)]
public class IndexModel(AssistantService assistant, TenantTime time) : CrmPageModel
{
    [BindProperty(SupportsGet = true, Name = "c")] public Guid? ConversationId { get; set; }
    [BindProperty(SupportsGet = true)] public string? EntityType { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? EntityId { get; set; }

    public bool Connected => assistant.IsConfigured;
    public List<ConversationRow> Conversations { get; private set; } = [];
    public ConversationView? Current { get; private set; }
    public Dictionary<int, DateTime> LineLocal { get; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        // "Ask the assistant" from a record starts a conversation about it.
        if (ConversationId is null && EntityType is not null && EntityId is not null)
            return RedirectToPage(new { c = await assistant.StartAsync(EntityType, EntityId) });
        Conversations = await assistant.ListAsync();
        if (ConversationId is { } id)
        {
            Current = await assistant.GetAsync(id);
            for (var i = 0; i < Current.Lines.Count; i++) LineLocal[i] = await time.ToLocalAsync(Current.Lines[i].At);
        }
        return Page();
    });

    public Task<IActionResult> OnPostSendAsync(string message) => Guard(async () =>
    {
        var id = ConversationId ?? await assistant.StartAsync(null, null);
        await assistant.SendAsync(id, message);
        return RedirectToPage(new { c = id });
    }, () => RedirectToPage(new { c = ConversationId }));

    /// <summary>
    /// The same as Send, streamed as server-sent events: start (the conversation id), text pieces, thinking,
    /// tool (a label while a lookup runs), then done (where to go) or error (why it couldn't send).
    /// </summary>
    public async Task<IActionResult> OnPostStreamAsync(string message)
    {
        var ct = HttpContext.RequestAborted;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        async Task Send(string type, object data)
        {
            await Response.WriteAsync($"event: {type}\ndata: {System.Text.Json.JsonSerializer.Serialize(data)}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        try
        {
            if (string.IsNullOrWhiteSpace(message)) throw new CrmRuleException("Type a message."); // before starting a conversation
            var id = ConversationId ?? await assistant.StartAsync(null, null);
            await Send("start", new { conversation = id });
            await assistant.SendAsync(id, message, e => e.Type == AssistantEvent.Tool
                ? Send(e.Type, new { tool = e.Value, label = AssistantTools.Label(e.Value) })
                : Send(e.Type, new { text = e.Value }), ct);
            await Send("done", new { conversation = id, url = $"/Assistant?c={id}" });
        }
        catch (CrmRuleException ex) { await Send("error", new { message = ex.Message }); }
        catch (RecordForbiddenException ex) { await Send("error", new { message = ex.Message }); }
        catch (RecordNotFoundException) { await Send("error", new { message = "That conversation wasn't found." }); }
        return new EmptyResult();
    }

    public Task<IActionResult> OnPostDeleteAsync() => Guard(async () =>
    {
        await assistant.DeleteAsync(ConversationId ?? Guid.Empty);
        Flash("Conversation deleted.");
        return RedirectToPage(new { c = (Guid?)null });
    });
}
