using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Crm;

namespace Oes.Web.ViewComponents;

public sealed record SavedViewsModel(string ListKey, List<SavedViewRow> Views, Dictionary<string, string> Current, bool CanShare, string ReturnUrl,
    string ResetUrl)
{
    /// <summary>The saved view whose filters are exactly what's on screen, if any.</summary>
    public SavedViewRow? Active => Views.FirstOrDefault(v => v.Criteria.Count == Current.Count
                                                             && v.Criteria.All(kv => Current.TryGetValue(kv.Key, out var c) && c == kv.Value));
}

/// <summary>The "Views" menu on list pages: pick, save, share, set default, delete.</summary>
public sealed class SavedViewsViewComponent(SavedViewService views) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string listKey)
    {
        var list = SavedViewService.List(listKey)!;
        var current = SavedViewService.Clean(list, Request.Query.Select(q => new KeyValuePair<string, string?>(q.Key, q.Value.ToString())));
        return View("/Pages/Shared/_SavedViews.cshtml", new SavedViewsModel(listKey, await views.ForListAsync(listKey), current,
            await views.CanShareAsync(), Request.Path + Request.QueryString, list.Path + "?reset=1"));
    }
}
