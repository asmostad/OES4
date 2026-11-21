using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

/// <summary>A list page that can have saved views: its path and the query-string filters a view keeps.</summary>
public sealed record SavedViewList(string Key, string Label, string Path, string[] Filters);

public sealed record SavedViewRow(Guid Id, string Name, bool Shared, bool IsDefault, bool CanDelete, string Url, Dictionary<string, string> Criteria);

/// <summary>
/// Saved filters for list pages. Only each list's known filter keys are kept (never paging),
/// and a view stores filters, not results, so it always shows what the viewer is allowed to see
/// ("mine" means the viewer). Shared views need records.view_all (managers and admins).
/// </summary>
public sealed class SavedViewService(OesDbContext db, RecordScope scope, IAuditLog audit)
{
    public static readonly IReadOnlyList<SavedViewList> Lists =
    [
        new("opportunities", "Opportunities", "/Opportunities", ["q", "view", "stage", "outcome", "mine", "storm"]),
        new("contacts", "Contacts", "/Contacts", ["q", "type", "status"]),
        new("properties", "Properties", "/Properties", ["q", "kind"]),
        new("projects", "Projects", "/Projects", ["q", "view", "mine", "removed"]),
        new("estimates", "Estimates", "/Estimates", ["q", "status", "mine"]),
        new("claims", "Claims", "/Claims", ["q", "status", "storm", "carrier"]),
        new("tasks", "Tasks", "/Tasks", ["all", "done"]),
    ];

    public const int MaxPerUser = 30;

    public static SavedViewList? List(string key) => Lists.FirstOrDefault(l => l.Key == key);

    /// <summary>Keeps only the list's filters, trimmed and non-empty, in the list's own key order.</summary>
    public static Dictionary<string, string> Clean(SavedViewList list, IEnumerable<KeyValuePair<string, string?>> query)
    {
        var given = query.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .GroupBy(kv => kv.Key.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Value!.Trim());
        var result = new Dictionary<string, string>();
        foreach (var key in list.Filters)
            if (given.TryGetValue(key, out var v) && v.Length <= 200 && !(v == "false" && key is "mine" or "removed" or "all" or "done"))
                result[key] = v;
        return result;
    }

    public static string Url(SavedViewList list, IReadOnlyDictionary<string, string> criteria) =>
        criteria.Count == 0 ? list.Path + "?reset=1"
            : list.Path + "?" + string.Join("&", criteria.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    public async Task<bool> CanShareAsync() => await scope.ViewAllAsync();

    public async Task<List<SavedViewRow>> ForListAsync(string listKey, CancellationToken ct = default)
    {
        var list = List(listKey) ?? throw new CrmRuleException("Unknown list.");
        var me = scope.Me;
        var canShare = await CanShareAsync();
        var views = await db.SavedViews.Where(v => v.ListKey == listKey && (v.UserId == me || v.UserId == null))
            .OrderBy(v => v.UserId == null).ThenBy(v => v.Name).ToListAsync(ct);
        return views.Select(v =>
        {
            var criteria = Read(v.CriteriaJson);
            return new SavedViewRow(v.Id, v.Name, v.UserId is null, v.IsDefault, v.UserId == me || canShare, Url(list, criteria), criteria);
        }).ToList();
    }

    /// <summary>Saves the current filters under a name; the same name on the same list replaces the old view.</summary>
    public async Task<Guid> SaveAsync(string listKey, string name, IEnumerable<KeyValuePair<string, string?>> query, bool shared, bool isDefault,
        CancellationToken ct = default)
    {
        var list = List(listKey) ?? throw new CrmRuleException("Unknown list.");
        var me = scope.Me ?? throw new RecordForbiddenException("Sign in first.");
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 80) throw new CrmRuleException("Give the view a name (up to 80 characters).");
        if (shared && !await CanShareAsync()) throw new RecordForbiddenException("Only managers can share views with everyone.");
        if (shared && isDefault) throw new CrmRuleException("A shared view can't be your default; save a personal copy for that.");
        Guid? owner = shared ? null : me;

        var view = await db.SavedViews.FirstOrDefaultAsync(v => v.ListKey == listKey && v.UserId == owner && v.Name == name, ct);
        if (view is null)
        {
            if (!shared && await db.SavedViews.CountAsync(v => v.UserId == me, ct) >= MaxPerUser)
                throw new CrmRuleException($"You can keep up to {MaxPerUser} saved views. Delete one first.");
            view = new SavedView { ListKey = listKey, UserId = owner, Name = name };
            db.SavedViews.Add(view);
        }
        view.CriteriaJson = JsonSerializer.Serialize(Clean(list, query));
        if (isDefault) await ClearDefaultAsync(listKey, me, ct);
        view.IsDefault = isDefault;
        if (shared) audit.Record("saved_view.shared", "SavedView", view.Id, $"Shared view \"{name}\" on {list.Label}");
        await db.SaveChangesAsync(ct);
        return view.Id;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var view = await VisibleAsync(id, ct);
        if (view.UserId != scope.Me)
        {
            if (!await CanShareAsync()) throw new RecordForbiddenException("Only managers can remove shared views.");
            audit.Record("saved_view.deleted", "SavedView", view.Id, $"Removed shared view \"{view.Name}\"");
        }
        db.SavedViews.Remove(view);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Makes a personal view the default for its list (or turns the default off).</summary>
    public async Task SetDefaultAsync(Guid id, bool isDefault, CancellationToken ct = default)
    {
        var view = await VisibleAsync(id, ct);
        if (view.UserId != scope.Me) throw new CrmRuleException("Only your own views can be your default. Save a copy first.");
        if (isDefault && view.UserId is { } owner) await ClearDefaultAsync(view.ListKey, owner, ct);
        view.IsDefault = isDefault;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The URL of the user's default view for the list at <paramref name="path"/>, if they have one.</summary>
    public async Task<string?> DefaultUrlForPathAsync(string path, CancellationToken ct = default)
    {
        var list = Lists.FirstOrDefault(l => string.Equals(l.Path, path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        if (list is null || scope.Me is not { } me) return null;
        var json = await db.SavedViews.Where(v => v.ListKey == list.Key && v.UserId == me && v.IsDefault).Select(v => v.CriteriaJson).FirstOrDefaultAsync(ct);
        return json is null ? null : Url(list, Read(json));
    }

    private async Task<SavedView> VisibleAsync(Guid id, CancellationToken ct)
    {
        var me = scope.Me;
        return await db.SavedViews.FirstOrDefaultAsync(v => v.Id == id && (v.UserId == me || v.UserId == null), ct)
               ?? throw new RecordNotFoundException("Saved view");
    }

    private async Task ClearDefaultAsync(string listKey, Guid me, CancellationToken ct)
    {
        foreach (var v in await db.SavedViews.Where(v => v.ListKey == listKey && v.UserId == me && v.IsDefault).ToListAsync(ct))
            v.IsDefault = false;
    }

    private static Dictionary<string, string> Read(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
