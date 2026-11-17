using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

public sealed class StageInput
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3b82f6";
    public int? StaleAfterHours { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Tenant-editable pipeline stages and pick lists. Code keys off stage outcomes and lookup
/// keys, never labels, so renaming is always safe.
/// </summary>
public sealed partial class ListSettingsService(OesDbContext db, RecordScope scope, IAuditLog audit)
{
    public static readonly Dictionary<string, string> EditableLists = new()
    {
        [LookupTypes.LeadSource] = "Lead sources",
        [LookupTypes.PropertyContactRole] = "Property contact roles",
        [LookupTypes.Peril] = "Perils",
        [LookupTypes.LostReason] = "Lost reasons",
        [LookupTypes.DocumentCategory] = "Document categories",
    };

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    public Task<List<PipelineStage>> StagesAsync(CancellationToken ct = default) =>
        db.PipelineStages.OrderBy(s => s.Sort).ToListAsync(ct);

    public async Task SaveStageAsync(StageInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SettingsManage);
        var name = input.Name.Trim();
        if (name.Length == 0) throw new CrmRuleException("Stage name is required.");
        if (!HexColor().IsMatch(input.Color)) throw new CrmRuleException("Color must look like #3b82f6.");
        if (input.StaleAfterHours is < 1) throw new CrmRuleException("Stale-after must be at least 1 hour.");

        PipelineStage stage;
        if (input.Id is { } id)
        {
            stage = await db.PipelineStages.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new RecordNotFoundException("Stage");
            if (!input.IsActive && stage.Outcome != Outcomes.Open)
                throw new CrmRuleException("The won and lost stages can be renamed but not turned off.");
            if (!input.IsActive && stage.IsDefault) throw new CrmRuleException("Pick another default stage before turning this one off.");
            if (!input.IsActive && await db.Opportunities.AnyAsync(o => o.StageId == id && o.ArchivedAt == null && o.Outcome == Outcomes.Open, ct))
                throw new CrmRuleException("Move the open opportunities out of this stage before turning it off.");
        }
        else
        {
            var last = await db.PipelineStages.Where(s => s.Outcome == Outcomes.Open).MaxAsync(s => (int?)s.Sort, ct) ?? 0;
            stage = new PipelineStage { Outcome = Outcomes.Open, Sort = last + 5 };
            db.PipelineStages.Add(stage);
        }
        stage.Name = name;
        stage.Color = input.Color.ToLowerInvariant();
        stage.StaleAfterHours = stage.Outcome == Outcomes.Open ? input.StaleAfterHours : null;
        stage.IsActive = input.IsActive;
        audit.Record(input.Id is null ? "stage.created" : "stage.updated", "PipelineStage", stage.Id, $"Stage {name}");
        await db.SaveChangesAsync(ct);
    }

    public async Task MoveStageAsync(Guid id, int direction, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SettingsManage);
        var open = await db.PipelineStages.Where(s => s.Outcome == Outcomes.Open).OrderBy(s => s.Sort).ToListAsync(ct);
        var i = open.FindIndex(s => s.Id == id);
        var j = i + Math.Sign(direction);
        if (i < 0 || j < 0 || j >= open.Count) return;
        (open[i].Sort, open[j].Sort) = (open[j].Sort, open[i].Sort);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetDefaultStageAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SettingsManage);
        var stages = await db.PipelineStages.ToListAsync(ct);
        var target = stages.FirstOrDefault(s => s.Id == id) ?? throw new RecordNotFoundException("Stage");
        if (target.Outcome != Outcomes.Open || !target.IsActive) throw new CrmRuleException("The default stage must be an active open stage.");
        foreach (var s in stages) s.IsDefault = s.Id == id;
        await db.SaveChangesAsync(ct);
    }

    public Task<List<LookupValue>> ValuesAsync(string type, CancellationToken ct = default) =>
        db.LookupValues.Where(l => l.Type == type).OrderBy(l => l.Sort).ThenBy(l => l.Label).ToListAsync(ct);

    public async Task SaveValueAsync(string type, Guid? id, string label, bool isActive, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SettingsManage);
        if (!EditableLists.ContainsKey(type)) throw new CrmRuleException("Unknown list.");
        label = label.Trim();
        if (label.Length == 0) throw new CrmRuleException("Label is required.");

        LookupValue v;
        if (id is { } vid)
        {
            v = await db.LookupValues.FirstOrDefaultAsync(l => l.Id == vid && l.Type == type, ct) ?? throw new RecordNotFoundException("Value");
        }
        else
        {
            // Keys are stable identifiers derived once from the first label; renaming never changes them.
            var key = KeyFrom(label);
            var existing = await db.LookupValues.Where(l => l.Type == type).Select(l => l.Key).ToListAsync(ct);
            var unique = key;
            for (var n = 2; existing.Contains(unique); n++) unique = $"{key}_{n}";
            var sort = (await db.LookupValues.Where(l => l.Type == type).MaxAsync(l => (int?)l.Sort, ct) ?? 0) + 10;
            v = new LookupValue { Type = type, Key = unique, Sort = sort };
            db.LookupValues.Add(v);
        }
        v.Label = label;
        v.IsActive = isActive;
        await db.SaveChangesAsync(ct);
    }

    private static string KeyFrom(string label)
    {
        var chars = label.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var key = Regex.Replace(new string(chars), "_+", "_").Trim('_');
        return key.Length == 0 ? "value" : key.Length > 50 ? key[..50] : key;
    }
}
