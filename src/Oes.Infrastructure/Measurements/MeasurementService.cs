using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Measurements;

public sealed class MeasurementInput
{
    public string Source { get; set; } = "manual";
    public DateOnly? MeasuredOn { get; set; }
    public string? Notes { get; set; }

    /// <summary>Keyed "surface|field" ("|total_area", "front|wall_area"). Blank values are skipped.</summary>
    public Dictionary<string, decimal?> Values { get; set; } = [];
}

public sealed record SetSummary(
    Guid Id, Guid StructureId, string StructureName, Guid TradeTypeId, string TradeName, string TradeColor, string TradeIcon, string Mode,
    int VersionNo, int VersionCount, string Source, DateOnly? MeasuredOn, DateTime CreatedAt, string? MeasuredBy, string Headline);

public sealed record SetDetail(
    MeasurementSet Set, Property Property, PropertyStructure Structure, TradeType Trade, List<MeasurementFieldDef> Fields,
    MeasurementSet? Previous, List<SetVersionRow> Versions, Document? SourceDocument, string? MeasuredBy, bool CanEdit)
{
    public decimal? Value(string surface, string field) =>
        Set.Values.FirstOrDefault(v => v.SurfaceKey == surface && v.FieldKey == field)?.Value;

    public decimal? PreviousValue(string surface, string field) =>
        Previous?.Values.FirstOrDefault(v => v.SurfaceKey == surface && v.FieldKey == field)?.Value;

    public decimal? Total(string field) =>
        Set.Values.Where(v => v.FieldKey == field).Select(v => (decimal?)v.Value).Sum();
}

public sealed record SetVersionRow(Guid Id, int VersionNo, bool IsCurrent, string Source, DateOnly? MeasuredOn, DateTime CreatedAt);

public sealed record PropertyMeasurements(Property Property, List<PropertyStructure> Structures, List<TradeType> Trades, List<SetSummary> Current);

/// <summary>
/// Measurements per property, structure and trade (design decision 3, items 3–4). Versions are
/// never overwritten; the source report is kept as a property document.
/// </summary>
public sealed partial class MeasurementService(OesDbContext db, RecordScope scope, RecordItems items, IClock clock, IAuditLog audit)
{
    public Task<List<TradeType>> TradesAsync(bool activeOnly = true, CancellationToken ct = default) =>
        db.TradeTypes.Where(t => !activeOnly || t.IsActive).OrderBy(t => t.Sort).ToListAsync(ct);

    public Task<List<MeasurementFieldDef>> FieldsAsync(Guid tradeTypeId, bool activeOnly = true, CancellationToken ct = default) =>
        db.MeasurementFieldDefs.Where(f => f.TradeTypeId == tradeTypeId && (!activeOnly || f.IsActive)).OrderBy(f => f.Sort).ToListAsync(ct);

    public async Task<PropertyMeasurements> ForPropertyAsync(Guid propertyId, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(EntityTypes.Property, propertyId);
        var property = await db.Properties.FirstAsync(p => p.Id == propertyId, ct);
        var structures = await db.PropertyStructures.Where(s => s.PropertyId == propertyId).OrderBy(s => s.Sort).ToListAsync(ct);
        var trades = await TradesAsync(true, ct);
        var current = await SummariesAsync(db.MeasurementSets.Where(s => s.PropertyId == propertyId && s.IsCurrent), ct);
        return new PropertyMeasurements(property, structures, trades.Where(t => t.MeasurementMode != MeasurementModes.None).ToList(), current);
    }

    /// <summary>Most recent measurement versions across the properties the user can see.</summary>
    public async Task<List<(SetSummary Set, Property Property)>> RecentAsync(int take = 50, CancellationToken ct = default)
    {
        var props = await scope.PropertiesAsync();
        var sets = db.MeasurementSets.Where(s => props.Any(p => p.Id == s.PropertyId)).OrderByDescending(s => s.CreatedAt).Take(take);
        var summaries = await SummariesAsync(sets, ct);
        var ids = summaries.Select(s => s.Id).ToList();
        var propIds = await db.MeasurementSets.Where(s => ids.Contains(s.Id)).Select(s => new { s.Id, s.PropertyId }).ToListAsync(ct);
        var pids = propIds.Select(p => p.PropertyId).Distinct().ToList();
        var properties = await db.Properties.Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return summaries.OrderByDescending(s => s.CreatedAt)
            .Select(s => (s, properties[propIds.First(p => p.Id == s.Id).PropertyId])).ToList();
    }

    private async Task<List<SetSummary>> SummariesAsync(IQueryable<MeasurementSet> sets, CancellationToken ct)
    {
        var rows = await (from s in sets
                          join st in db.PropertyStructures on s.StructureId equals st.Id
                          join t in db.TradeTypes on s.TradeTypeId equals t.Id
                          join u in db.Users on s.MeasuredByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                          select new
                          {
                              s.Id, s.StructureId, StructureName = st.Name, s.TradeTypeId, TradeName = t.Name, t.Color, t.Icon, t.MeasurementMode,
                              s.VersionNo, s.Source, s.MeasuredOn, s.CreatedAt, By = u == null ? null : u.FirstName + " " + u.LastName,
                              Versions = db.MeasurementSets.Count(x => x.StructureId == s.StructureId && x.TradeTypeId == s.TradeTypeId),
                          }).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var values = await db.MeasurementValues.Where(v => ids.Contains(v.SetId)).ToListAsync(ct);
        return rows.Select(r => new SetSummary(r.Id, r.StructureId, r.StructureName, r.TradeTypeId, r.TradeName, r.Color, r.Icon, r.MeasurementMode,
            r.VersionNo, r.Versions, r.Source, r.MeasuredOn, r.CreatedAt, r.By, Headline(r.MeasurementMode, values.Where(v => v.SetId == r.Id).ToList()))).ToList();
    }

    /// <summary>One-line summary: squares for roofs, total wall area for elevations, the first run for linear trades.</summary>
    public static string Headline(string mode, List<MeasurementValue> values)
    {
        decimal? Sum(string key) => values.Where(v => v.FieldKey == key).Select(v => (decimal?)v.Value).Sum();
        var c = CultureInfo.GetCultureInfo("en-US");
        switch (mode)
        {
            case MeasurementModes.Roof:
                var area = Sum("total_area");
                var pitch = Sum("predominant_pitch");
                return area is null ? "No roof area" :
                    $"{area / 100m:0.##} sq" + (pitch is null ? "" : $" · {pitch:0.#}/12") + (Sum("valleys") is { } v ? $" · {v:0} LF valleys" : "");
            case MeasurementModes.Elevation:
                var wall = Sum("wall_area");
                var net = wall - (Sum("openings_area") ?? 0);
                return wall is null ? "No wall area" : $"{wall.Value.ToString("N0", c)} sq ft walls" + (net != wall ? $" · {net!.Value.ToString("N0", c)} net" : "");
            default:
                // Lead with the main run or count when the trade has one, then the rest as a count.
                var parts = new List<string>();
                if (Sum("gutter_length") is { } g) parts.Add($"{g:0} LF gutter");
                if (Sum("downspouts") is { } d) parts.Add($"{d:0} downspouts");
                if (Sum("fence_length") is { } f) parts.Add($"{f:0} LF fence");
                if (Sum("gates") is { } gt) parts.Add($"{gt:0} gates");
                if (Sum("windows") is { } w) parts.Add($"{w:0} windows");
                if (Sum("united_inches") is { } ui) parts.Add($"{ui:0} UI");
                if (parts.Count > 0) return string.Join(" · ", parts);
                return values.Count == 0 ? "No values" : $"{values.Count} value{(values.Count == 1 ? "" : "s")}";
        }
    }

    public async Task<SetDetail> GetSetAsync(Guid id, CancellationToken ct = default)
    {
        var set = await db.MeasurementSets.Include(s => s.Values).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new RecordNotFoundException("Measurements");
        await scope.EnsureCanSeeAsync(EntityTypes.Property, set.PropertyId);
        var property = await db.Properties.FirstAsync(p => p.Id == set.PropertyId, ct);
        var structure = await db.PropertyStructures.FirstAsync(s => s.Id == set.StructureId, ct);
        var trade = await db.TradeTypes.FirstAsync(t => t.Id == set.TradeTypeId, ct);
        // Show every field that is active or has a value in this version (retired fields keep their history).
        var used = set.Values.Select(v => v.FieldKey).ToHashSet();
        var fields = (await FieldsAsync(trade.Id, activeOnly: false, ct)).Where(f => f.IsActive || used.Contains(f.Key)).ToList();
        var previous = await db.MeasurementSets.Include(s => s.Values)
            .Where(s => s.StructureId == set.StructureId && s.TradeTypeId == set.TradeTypeId && s.VersionNo < set.VersionNo)
            .OrderByDescending(s => s.VersionNo).FirstOrDefaultAsync(ct);
        var versions = await db.MeasurementSets.Where(s => s.StructureId == set.StructureId && s.TradeTypeId == set.TradeTypeId)
            .OrderByDescending(s => s.VersionNo).Select(s => new SetVersionRow(s.Id, s.VersionNo, s.IsCurrent, s.Source, s.MeasuredOn, s.CreatedAt)).ToListAsync(ct);
        var doc = set.SourceDocumentId is null ? null : await db.Documents.FirstOrDefaultAsync(d => d.Id == set.SourceDocumentId && d.ArchivedAt == null, ct);
        var by = set.MeasuredByUserId is null ? null
            : await db.Users.Where(u => u.Id == set.MeasuredByUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);
        return new SetDetail(set, property, structure, trade, fields, previous, versions, doc, by, await CanMeasureAsync());
    }

    public async Task<bool> CanMeasureAsync() =>
        await scope.CanEditAsync(null) && await HasPermissionAsync(Permissions.MeasurementsManage);

    private async Task<bool> HasPermissionAsync(string key)
    {
        try { await scope.EnsurePermissionAsync(key); return true; }
        catch (RecordForbiddenException) { return false; }
    }

    /// <summary>The latest version for a structure + trade, to prefill a new version from.</summary>
    public async Task<MeasurementSet?> CurrentAsync(Guid structureId, Guid tradeTypeId, CancellationToken ct = default) =>
        await db.MeasurementSets.Include(s => s.Values)
            .FirstOrDefaultAsync(s => s.StructureId == structureId && s.TradeTypeId == tradeTypeId && s.IsCurrent, ct);

    /// <summary>Saves a new version (never edits an old one). An optional report file is stored on the property.</summary>
    public async Task<Guid> SaveVersionAsync(Guid propertyId, Guid structureId, Guid tradeTypeId, MeasurementInput input,
        (Stream Content, string FileName, string ContentType)? report = null, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.MeasurementsManage);
        await scope.EnsureCanSeeAsync(EntityTypes.Property, propertyId);
        var structure = await db.PropertyStructures.FirstOrDefaultAsync(s => s.Id == structureId && s.PropertyId == propertyId, ct)
                        ?? throw new CrmRuleException("That structure isn't on this property.");
        var trade = await db.TradeTypes.FirstOrDefaultAsync(t => t.Id == tradeTypeId && t.IsActive, ct) ?? throw new CrmRuleException("Unknown trade.");
        if (trade.MeasurementMode == MeasurementModes.None) throw new CrmRuleException($"{trade.Name} doesn't take measurements.");
        if (!MeasurementSources.All.Contains(input.Source)) throw new CrmRuleException("Unknown measurement source.");

        var fields = (await FieldsAsync(trade.Id, true, ct)).Select(f => f.Key).ToHashSet();
        var surfaces = MeasurementModes.Surfaces(trade.MeasurementMode).ToHashSet();
        var values = new List<MeasurementValue>();
        foreach (var (key, value) in input.Values)
        {
            if (value is null) continue;
            var parts = key.Split('|');
            if (parts.Length != 2 || !surfaces.Contains(parts[0]) || !fields.Contains(parts[1]))
                throw new CrmRuleException($"Unknown measurement '{key}'.");
            if (value < 0) throw new CrmRuleException("Measurements can't be negative.");
            if (value > 10_000_000) throw new CrmRuleException("That measurement looks too large.");
            values.Add(new MeasurementValue { SurfaceKey = parts[0], FieldKey = parts[1], Value = value.Value });
        }
        if (values.Count == 0) throw new CrmRuleException("Enter at least one measurement.");

        Guid? documentId = null;
        if (report is { } r)
            documentId = await items.UploadAsync(EntityTypes.Property, propertyId, r.Content, r.FileName, r.ContentType, "measurement_report", ct);

        var previous = await db.MeasurementSets.Where(s => s.StructureId == structureId && s.TradeTypeId == tradeTypeId)
            .OrderByDescending(s => s.VersionNo).FirstOrDefaultAsync(ct);
        foreach (var cur in await db.MeasurementSets.Where(s => s.StructureId == structureId && s.TradeTypeId == tradeTypeId && s.IsCurrent).ToListAsync(ct))
            cur.IsCurrent = false;

        var set = new MeasurementSet
        {
            PropertyId = propertyId, StructureId = structureId, TradeTypeId = tradeTypeId,
            VersionNo = (previous?.VersionNo ?? 0) + 1, IsCurrent = true, Source = input.Source, SourceDocumentId = documentId,
            MeasuredOn = input.MeasuredOn ?? DateOnly.FromDateTime(clock.UtcNow), MeasuredByUserId = scope.Me,
            SupersedesId = previous?.Id, Notes = ContactService.Clean(input.Notes),
        };
        foreach (var v in values) v.SetId = set.Id;
        set.Values.AddRange(values);
        db.MeasurementSets.Add(set);
        await items.LogAsync(EntityTypes.Property, propertyId, "measurements.saved",
            $"{trade.Name} measurements v{set.VersionNo} for {structure.Name} ({SourceLabel(set.Source)})", ct);
        await db.SaveChangesAsync(ct);
        return set.Id;
    }

    /// <summary>Makes an older version current again (e.g. a new report was wrong). Nothing is deleted.</summary>
    public async Task MakeCurrentAsync(Guid setId, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.MeasurementsManage);
        var set = await db.MeasurementSets.FirstOrDefaultAsync(s => s.Id == setId, ct) ?? throw new RecordNotFoundException("Measurements");
        await scope.EnsureCanSeeAsync(EntityTypes.Property, set.PropertyId);
        foreach (var s in await db.MeasurementSets.Where(s => s.StructureId == set.StructureId && s.TradeTypeId == set.TradeTypeId).ToListAsync(ct))
            s.IsCurrent = s.Id == setId;
        var trade = await db.TradeTypes.Where(t => t.Id == set.TradeTypeId).Select(t => t.Name).FirstAsync(ct);
        await items.LogAsync(EntityTypes.Property, set.PropertyId, "measurements.reverted", $"{trade} measurements v{set.VersionNo} made current again", ct);
        await db.SaveChangesAsync(ct);
    }

    public static string SourceLabel(string source) => source switch
    {
        "eagleview" => "EagleView",
        "hover" => "Hover",
        "gaf_quickmeasure" => "GAF QuickMeasure",
        "roofr" => "Roofr",
        "xactimate" => "Xactimate",
        "field_tape" => "Field tape",
        "manual" => "Manual entry",
        _ => "Other",
    };

    // ---------------- Admin: trades and fields ----------------

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    public async Task<Guid> SaveTradeAsync(Guid? id, string name, string mode, string color, bool isActive, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ProductsManage);
        name = name.Trim();
        if (name.Length == 0) throw new CrmRuleException("Trade name is required.");
        if (!MeasurementModes.All.Contains(mode)) throw new CrmRuleException("Unknown measurement mode.");
        if (!HexColor().IsMatch(color)) throw new CrmRuleException("Color must look like #3b82f6.");

        TradeType trade;
        if (id is { } tid)
        {
            trade = await db.TradeTypes.FirstOrDefaultAsync(t => t.Id == tid, ct) ?? throw new RecordNotFoundException("Trade");
            if (trade.MeasurementMode != mode && await db.MeasurementSets.AnyAsync(s => s.TradeTypeId == tid, ct))
                throw new CrmRuleException("This trade already has measurements, so its measurement mode can't change.");
        }
        else
        {
            var code = KeyFrom(name);
            var codes = await db.TradeTypes.Select(t => t.Code).ToListAsync(ct);
            for (var n = 2; codes.Contains(code); n++) code = $"{KeyFrom(name)}_{n}";
            trade = new TradeType { Code = code, Sort = (await db.TradeTypes.MaxAsync(t => (int?)t.Sort, ct) ?? 0) + 10, Icon = "bi-hammer" };
            db.TradeTypes.Add(trade);
        }
        trade.Name = name;
        trade.MeasurementMode = mode;
        trade.Color = color.ToLowerInvariant();
        trade.IsActive = isActive;
        audit.Record(id is null ? "trade.created" : "trade.updated", "TradeType", trade.Id, $"Trade {name}");
        await db.SaveChangesAsync(ct);
        return trade.Id;
    }

    public async Task SaveFieldAsync(Guid tradeTypeId, Guid? id, string label, string unit, string? group, bool isActive, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ProductsManage);
        label = label.Trim();
        if (label.Length == 0) throw new CrmRuleException("Field label is required.");
        if (!await db.TradeTypes.AnyAsync(t => t.Id == tradeTypeId, ct)) throw new RecordNotFoundException("Trade");

        MeasurementFieldDef f;
        if (id is { } fid)
        {
            f = await db.MeasurementFieldDefs.FirstOrDefaultAsync(x => x.Id == fid && x.TradeTypeId == tradeTypeId, ct) ?? throw new RecordNotFoundException("Field");
        }
        else
        {
            var key = KeyFrom(label);
            var keys = await db.MeasurementFieldDefs.Where(x => x.TradeTypeId == tradeTypeId).Select(x => x.Key).ToListAsync(ct);
            for (var n = 2; keys.Contains(key); n++) key = $"{KeyFrom(label)}_{n}";
            f = new MeasurementFieldDef
            {
                TradeTypeId = tradeTypeId, Key = key,
                Sort = (await db.MeasurementFieldDefs.Where(x => x.TradeTypeId == tradeTypeId).MaxAsync(x => (int?)x.Sort, ct) ?? 0) + 10,
            };
            db.MeasurementFieldDefs.Add(f);
        }
        f.Label = label;
        f.Unit = unit.Trim();
        f.Group = string.IsNullOrWhiteSpace(group) ? null : group.Trim();
        f.IsActive = isActive;
        await db.SaveChangesAsync(ct);
    }

    private static string KeyFrom(string label)
    {
        var key = Regex.Replace(new string(label.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()), "_+", "_").Trim('_');
        return key.Length == 0 ? "field" : key.Length > 50 ? key[..50] : key;
    }
}
