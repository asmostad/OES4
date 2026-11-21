using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Ai;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

/// <summary>The AI knowledge base: the articles, and a search box that shows what the assistant would find.</summary>
[HasPermission(Permissions.AiManage)]
public class KnowledgeModel(KnowledgeService knowledge) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<KnowledgeRow> Articles { get; private set; } = [];
    public List<KnowledgeHit>? Hits { get; private set; }

    public async Task OnGetAsync()
    {
        Articles = await knowledge.ListAsync();
        if (!string.IsNullOrWhiteSpace(Q)) Hits = await knowledge.SearchAsync(Q, 5);
    }
}
