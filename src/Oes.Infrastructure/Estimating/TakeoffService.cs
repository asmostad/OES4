using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Estimating;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Estimating;

public sealed class TakeoffRuleInput
{
    public Guid? Id { get; set; }
    public Guid TradeTypeId { get; set; }
    public string Name { get; set; } = "";
    public Guid? ProductId { get; set; }
    public string? Slot { get; set; }
    public string Driver { get; set; } = TakeoffEngine.Flat;
    public decimal Multiplier { get; set; } = 1m;
    public decimal WastePercent { get; set; }
    public bool UseSuggestedWaste { get; set; }
    public string RoundMode { get; set; } = TakeoffRounding.Up;
    public decimal RoundTo { get; set; } = 1m;
    public decimal MinQty { get; set; }
    public decimal? MaxQty { get; set; }
    public string? Cond1Field { get; set; }
    public string? Cond1Op { get; set; }
    public decimal? Cond1Value { get; set; }
    public string? Cond2Field { get; set; }
    public string? Cond2Op { get; set; }
    public decimal? Cond2Value { get; set; }
    public string? OptionKey { get; set; }
    public string? OptionLabel { get; set; }
    public bool OptionDefaultOn { get; set; } = true;
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record TakeoffRuleRow(TakeoffRule Rule, string? ProductName);

public sealed record TakeoffOption(string Key, string Label, bool DefaultOn);

/// <summary>Everything the takeoff page needs for one estimate section.</summary>
public sealed record SectionTakeoff(
    EstimateSection Section, TradeType Trade, List<TakeoffDriver> Drivers, Dictionary<string, decimal> Values, bool HasMeasurements,
    Dictionary<string, List<Product>> SlotProducts, List<TakeoffOption> Options, TakeoffChoicesInput Choices, List<TakeoffLine> Lines);

/// <summary>A takeoff over one trade's measurements (an estimate section, or a project trade's order sheet).</summary>
public sealed record TakeoffRun(
    TradeType Trade, List<TakeoffDriver> Drivers, Dictionary<string, decimal> Values, bool HasMeasurements,
    Dictionary<string, List<Product>> SlotProducts, List<TakeoffOption> Options, TakeoffChoicesInput Choices, List<TakeoffLine> Lines);

public sealed class TakeoffChoicesInput
{
    public Dictionary<string, Guid?> Slots { get; set; } = [];
    public List<string> Options { get; set; } = [];
}

/// <summary>Takeoff rules (Administration › Takeoff Rules, products.manage) and running them on estimate sections.</summary>
public sealed class TakeoffService(OesDbContext db, RecordScope scope, EstimateService estimates, IAuditLog audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<List<TakeoffRuleRow>> RulesAsync(Guid tradeTypeId, CancellationToken ct = default) =>
        (await (from r in db.TakeoffRules where r.TradeTypeId == tradeTypeId
                join p in db.Products on r.ProductId equals p.Id into ps from p in ps.DefaultIfEmpty()
                orderby r.Sort, r.Name
                select new { r, Product = p == null ? null : p.Name }).ToListAsync(ct))
        .Select(x => new TakeoffRuleRow(x.r, x.Product)).ToList();

    /// <summary>Every slot in use by a rule or a product, for pickers.</summary>
    public async Task<List<string>> SlotsAsync(CancellationToken ct = default) =>
        (await db.TakeoffRules.Where(r => r.Slot != null).Select(r => r.Slot!).Distinct().ToListAsync(ct))
        .Union(await db.Products.Where(p => p.TakeoffSlot != null).Select(p => p.TakeoffSlot!).Distinct().ToListAsync(ct))
        .Distinct().Order().ToList();

    public async Task<List<TakeoffDriver>> DriversAsync(Guid tradeTypeId, CancellationToken ct = default)
    {
        var trade = await db.TradeTypes.FirstOrDefaultAsync(t => t.Id == tradeTypeId, ct) ?? throw new RecordNotFoundException("Trade");
        return TakeoffEngine.Drivers(trade, await db.MeasurementFieldDefs.Where(f => f.TradeTypeId == tradeTypeId).ToListAsync(ct));
    }

    public async Task<TakeoffRule> GetRuleAsync(Guid id, CancellationToken ct = default) =>
        await db.TakeoffRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new RecordNotFoundException("Takeoff rule");

    public async Task<Guid> SaveRuleAsync(TakeoffRuleInput i, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ProductsManage);
        var name = i.Name.Trim();
        if (name.Length is 0 or > 150) throw new CrmRuleException("Give the rule a name.");
        var drivers = (await DriversAsync(i.TradeTypeId, ct)).Select(d => d.Key).ToHashSet();
        if (!drivers.Contains(i.Driver)) throw new CrmRuleException("Pick what the quantity is based on.");
        var slot = Key(i.Slot);
        if (slot is null && i.ProductId is null) throw new CrmRuleException("Pick a product, or a slot the rep fills on each job.");
        if (slot is null && i.ProductId is { } pid && !await db.Products.AnyAsync(p => p.Id == pid, ct)) throw new CrmRuleException("Unknown product.");
        if (i.Multiplier <= 0) throw new CrmRuleException("The multiplier must be more than zero.");
        if (i.WastePercent is < 0 or > 100) throw new CrmRuleException("Waste must be between 0 and 100%.");
        if (!TakeoffRounding.All.Contains(i.RoundMode)) throw new CrmRuleException("Pick a rounding.");
        if (i.RoundTo <= 0) throw new CrmRuleException("Round to a multiple more than zero (1 = whole units).");
        if (i.MinQty < 0 || i.MaxQty is < 0 || (i.MaxQty is { } max && max < i.MinQty)) throw new CrmRuleException("Check the minimum and maximum.");
        var (c1f, c1o, c1v) = Condition(i.Cond1Field, i.Cond1Op, i.Cond1Value, drivers);
        var (c2f, c2o, c2v) = Condition(i.Cond2Field, i.Cond2Op, i.Cond2Value, drivers);
        var option = Key(i.OptionKey);

        TakeoffRule r;
        if (i.Id is { } id) r = await GetRuleAsync(id, ct);
        else
        {
            r = new TakeoffRule { TradeTypeId = i.TradeTypeId, Sort = (await db.TakeoffRules.Where(x => x.TradeTypeId == i.TradeTypeId).MaxAsync(x => (int?)x.Sort, ct) ?? 0) + 10 };
            db.TakeoffRules.Add(r);
        }
        r.Name = name;
        r.Slot = slot;
        r.ProductId = slot is null ? i.ProductId : null;
        r.Driver = i.Driver;
        r.Multiplier = i.Multiplier;
        r.WastePercent = i.WastePercent;
        r.UseSuggestedWaste = i.UseSuggestedWaste;
        r.RoundMode = i.RoundMode;
        r.RoundTo = i.RoundTo;
        r.MinQty = i.MinQty;
        r.MaxQty = i.MaxQty;
        (r.Cond1Field, r.Cond1Op, r.Cond1Value) = (c1f, c1o, c1v);
        (r.Cond2Field, r.Cond2Op, r.Cond2Value) = (c2f, c2o, c2v);
        r.OptionKey = option;
        r.OptionLabel = option is null ? null : ContactService.Clean(i.OptionLabel) ?? TakeoffEngine.Label(option);
        r.OptionDefaultOn = i.OptionDefaultOn;
        r.Description = ContactService.Clean(i.Description);
        r.Notes = ContactService.Clean(i.Notes);
        r.IsActive = i.IsActive;
        audit.Record(i.Id is null ? "takeoff_rule.created" : "takeoff_rule.updated", "TakeoffRule", r.Id, $"Takeoff rule {name}");
        await db.SaveChangesAsync(ct);
        return r.Id;
    }

    public async Task MoveRuleAsync(Guid id, int direction, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ProductsManage);
        var rule = await GetRuleAsync(id, ct);
        var all = await db.TakeoffRules.Where(r => r.TradeTypeId == rule.TradeTypeId).OrderBy(r => r.Sort).ToListAsync(ct);
        var i = all.FindIndex(r => r.Id == id);
        var j = i + Math.Sign(direction);
        if (j < 0 || j >= all.Count) return;
        (all[i].Sort, all[j].Sort) = (all[j].Sort, all[i].Sort);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Running a takeoff on an estimate section ----------------

    public async Task<SectionTakeoff> ForSectionAsync(Guid estimateId, Guid sectionId, TakeoffChoicesInput? choices = null, CancellationToken ct = default)
    {
        var detail = await estimates.GetAsync(estimateId, ct); // visibility
        var section = detail.Sections.Select(s => s.Section).FirstOrDefault(s => s.Id == sectionId) ?? throw new RecordNotFoundException("Section");
        if (section.TradeTypeId is not { } tradeId) throw new CrmRuleException("Pick the section's trade before running a takeoff.");
        var setId = section.MeasurementSetId ?? (section.StructureId is { } sid
            ? await db.MeasurementSets.Where(s => s.StructureId == sid && s.TradeTypeId == tradeId && s.IsCurrent).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
            : null);
        var measured = setId is null ? new Dictionary<string, decimal>()
            : await db.MeasurementValues.Where(v => v.SetId == setId).GroupBy(v => v.FieldKey)
                .Select(g => new { g.Key, Sum = g.Sum(v => v.Value) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var run = await BuildAsync(tradeId, measured, section.TakeoffJson, choices, ct);
        return new SectionTakeoff(section, run.Trade, run.Drivers, run.Values, run.HasMeasurements, run.SlotProducts, run.Options, run.Choices, run.Lines);
    }

    /// <summary>
    /// Runs a trade's rules on measured values (field key to value). With no choices: the last run's
    /// (<paramref name="lastJson"/>), else defaults: options on by default, and a slot with one product picks it.
    /// </summary>
    public async Task<TakeoffRun> BuildAsync(Guid tradeId, Dictionary<string, decimal> measured, string? lastJson, TakeoffChoicesInput? choices,
        CancellationToken ct = default)
    {
        var trade = await db.TradeTypes.FirstAsync(t => t.Id == tradeId, ct);
        var fields = await db.MeasurementFieldDefs.Where(f => f.TradeTypeId == tradeId).ToListAsync(ct);
        var values = TakeoffEngine.Values(measured, fields);
        var rules = await db.TakeoffRules.Where(r => r.TradeTypeId == tradeId && r.IsActive).ToListAsync(ct);
        var slots = rules.Where(r => r.Slot != null).Select(r => r.Slot!).Distinct().ToList();
        var candidates = await db.Products.Where(p => p.IsActive && p.TakeoffSlot != null && slots.Contains(p.TakeoffSlot) && (p.TradeTypeId == null || p.TradeTypeId == tradeId))
            .OrderBy(p => p.Name).ToListAsync(ct);
        var slotProducts = slots.ToDictionary(s => s, s => candidates.Where(p => p.TakeoffSlot == s).ToList());
        var options = rules.Where(r => r.OptionKey != null).GroupBy(r => r.OptionKey!)
            .Select(g => new TakeoffOption(g.Key, g.First().OptionLabel ?? TakeoffEngine.Label(g.Key), g.First().OptionDefaultOn)).ToList();

        choices ??= Read(lastJson) ?? new TakeoffChoicesInput
        {
            Options = options.Where(o => o.DefaultOn).Select(o => o.Key).ToList(),
            Slots = slotProducts.Where(kv => kv.Value.Count == 1).ToDictionary(kv => kv.Key, kv => (Guid?)kv.Value[0].Id),
        };
        var picked = new Dictionary<string, Product>();
        foreach (var (slot, pid) in choices.Slots)
            if (pid is { } id && slotProducts.TryGetValue(slot, out var list) && list.FirstOrDefault(p => p.Id == id) is { } product) picked[slot] = product;
        var fixedIds = rules.Where(r => r.ProductId != null).Select(r => r.ProductId!.Value).ToList();
        var fixedProducts = await db.Products.Where(p => fixedIds.Contains(p.Id) && p.IsActive).ToDictionaryAsync(p => p.Id, ct);

        var lines = TakeoffEngine.Run(rules, values, fixedProducts, new TakeoffChoices(choices.Options.ToHashSet(), picked));
        return new TakeoffRun(trade, TakeoffEngine.Drivers(trade, fields), values, measured.Count > 0, slotProducts, options, choices, lines);
    }

    /// <summary>Runs the takeoff and replaces the section's generated lines. Returns how many lines were added.</summary>
    public async Task<int> ApplyAsync(Guid estimateId, Guid sectionId, TakeoffChoicesInput choices, CancellationToken ct = default)
    {
        var run = await ForSectionAsync(estimateId, sectionId, choices, ct);
        if (!run.HasMeasurements) throw new CrmRuleException("This section has no measurements to take off from.");
        return await estimates.ReplaceTakeoffLinesAsync(estimateId, sectionId, run.Lines, JsonSerializer.Serialize(choices, Json), ct);
    }

    public static string Write(TakeoffChoicesInput choices) => JsonSerializer.Serialize(choices, Json);

    private static TakeoffChoicesInput? Read(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<TakeoffChoicesInput>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static (string?, string?, decimal?) Condition(string? field, string? op, decimal? value, HashSet<string> drivers)
    {
        if (string.IsNullOrWhiteSpace(field)) return (null, null, null);
        if (!drivers.Contains(field)) throw new CrmRuleException("A condition uses an unknown measurement.");
        if (!TakeoffOps.All.Any(o => o.Key == op)) throw new CrmRuleException("Pick a comparison for each condition.");
        if (value is null) throw new CrmRuleException("Enter a value for each condition.");
        return (field, op, value);
    }

    /// <summary>Slot and option keys: lowercase letters, digits and underscores.</summary>
    internal static string? Key(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var k = new string(s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        while (k.Contains("__")) k = k.Replace("__", "_");
        return k.Length == 0 ? null : k[..Math.Min(k.Length, 50)];
    }
}
