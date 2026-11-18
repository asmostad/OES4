using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Ai;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Assistant;

/// <summary>Chat with the assistant. Answers can take a little while: the form posts and the page reloads with the reply.</summary>
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

    public Task<IActionResult> OnPostDeleteAsync() => Guard(async () =>
    {
        await assistant.DeleteAsync(ConversationId ?? Guid.Empty);
        Flash("Conversation deleted.");
        return RedirectToPage(new { c = (Guid?)null });
    });
}
