using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Ai;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

/// <summary>Write or paste a knowledge base article, or fill it from a text or Markdown file.</summary>
[HasPermission(Permissions.AiManage)]
public class KnowledgeEditModel(KnowledgeService knowledge) : CrmPageModel
{
    private const long MaxFileBytes = 400_000;

    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public KnowledgeInput Input { get; set; } = new();
    [BindProperty(Name = "File")] public IFormFile? Upload { get; set; }

    public List<string> Categories { get; private set; } = [];

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Id is { } id)
        {
            var a = await knowledge.GetAsync(id);
            Input = new KnowledgeInput { Title = a.Title, Category = a.Category, Body = a.Body, IsActive = a.IsActive };
        }
        Categories = await knowledge.CategoriesAsync();
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        Categories = await knowledge.CategoriesAsync();
        if (Upload is { Length: > 0 } f)
        {
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
            if (ext is not (".txt" or ".md" or ".markdown")) { ModelState.AddModelError("", "Upload a .txt or .md file, or paste the text."); return Page(); }
            if (f.Length > MaxFileBytes) { ModelState.AddModelError("", "That file is too big for one article. Split it up."); return Page(); }
            using var reader = new StreamReader(f.OpenReadStream());
            Input.Body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(Input.Title)) Input.Title = Path.GetFileNameWithoutExtension(f.FileName);
        }
        return await Guard(async () =>
        {
            Id = await knowledge.SaveAsync(Id, Input);
            Flash("Article saved. The assistant can search it now.");
            return RedirectToPage("/Admin/Knowledge");
        }, Page);
    }

    public Task<IActionResult> OnPostDeleteAsync() => Guard(async () =>
    {
        await knowledge.DeleteAsync(Id ?? Guid.Empty);
        Flash("Article deleted.");
        return RedirectToPage("/Admin/Knowledge");
    });
}
