using Oes.Domain.Estimating;
using Oes.Domain.Measurements;

namespace Oes.Infrastructure.Estimating;

/// <summary>A value a takeoff rule can be driven by or conditioned on.</summary>
public sealed record TakeoffDriver(string Key, string Label, string Unit);

/// <summary>What the person running the takeoff chose: options switched on and a product per slot.</summary>
public sealed record TakeoffChoices(IReadOnlySet<string> OptionsOn, IReadOnlyDictionary<string, Product> SlotProducts);

/// <summary>One rule's result. Lines with a <see cref="Problem"/> (no product) aren't added.</summary>
public sealed record TakeoffLine(TakeoffRule Rule, Product? Product, decimal Driver, decimal RawQty, decimal Qty, string Description, string? Problem);

/// <summary>
/// The takeoff calculation, with no stored formulas (nothing is evaluated): for each active rule
/// whose option is on and whose conditions hold, qty = driver × multiplier × (1 + waste%), then
/// rounded to a multiple, floored at the minimum and capped at the maximum. A product's own
/// coverage replaces the rule's multiplier. Zero quantities produce no line.
/// </summary>
public static class TakeoffEngine
{
    public const string Flat = "flat";

    /// <summary>
    /// The drivers for a trade: its measurement fields (summed across wall sides), "squares" for
    /// every sq ft field, and for roofs ridge + hip and eave + rake.
    /// </summary>
    public static List<TakeoffDriver> Drivers(TradeType trade, IEnumerable<MeasurementFieldDef> fields)
    {
        var list = new List<TakeoffDriver> { new(Flat, "Fixed quantity (1 per job)", "") };
        var defs = fields.Where(f => f.IsActive).OrderBy(f => f.Sort).ToList();
        foreach (var f in defs)
        {
            list.Add(new TakeoffDriver(f.Key, f.Label, f.Unit));
            if (f.Unit == "sq ft") list.Add(new TakeoffDriver(f.Key + "_squares", f.Label + " (squares)", "SQ"));
        }
        if (trade.MeasurementMode == MeasurementModes.Roof)
        {
            list.Add(new TakeoffDriver("ridge_hip", "Ridges + hips", "LF"));
            list.Add(new TakeoffDriver("eave_rake", "Eaves + rakes", "LF"));
        }
        return list;
    }

    /// <summary>Driver values from measurement values (field key → value, already summed across surfaces).</summary>
    public static Dictionary<string, decimal> Values(IReadOnlyDictionary<string, decimal> measured, IEnumerable<MeasurementFieldDef> fields)
    {
        var v = new Dictionary<string, decimal>(measured) { [Flat] = 1m };
        decimal M(string k) => measured.GetValueOrDefault(k);
        foreach (var f in fields.Where(f => f.Unit == "sq ft")) v[f.Key + "_squares"] = M(f.Key) / 100m;
        v["ridge_hip"] = M("ridges") + M("hips");
        v["eave_rake"] = M("eaves") + M("rakes");
        return v;
    }

    public static List<TakeoffLine> Run(IEnumerable<TakeoffRule> rules, IReadOnlyDictionary<string, decimal> drivers,
        IReadOnlyDictionary<Guid, Product> products, TakeoffChoices choices)
    {
        var result = new List<TakeoffLine>();
        foreach (var r in rules.Where(r => r.IsActive).OrderBy(r => r.Sort).ThenBy(r => r.Name))
        {
            if (r.OptionKey is { } opt && !choices.OptionsOn.Contains(opt)) continue;
            if (!Holds(r.Cond1Field, r.Cond1Op, r.Cond1Value, drivers) || !Holds(r.Cond2Field, r.Cond2Op, r.Cond2Value, drivers)) continue;

            Product? product = null;
            string? problem = null;
            if (r.Slot is { } slot)
            {
                if (!choices.SlotProducts.TryGetValue(slot, out product)) problem = $"Pick a product for “{Label(slot)}”.";
            }
            else if (r.ProductId is { } pid)
            {
                if (!products.TryGetValue(pid, out product)) problem = "Its product is inactive or missing.";
            }
            else problem = "No product is set on this rule.";

            var driver = drivers.GetValueOrDefault(r.Driver);
            var waste = r.UseSuggestedWaste && drivers.TryGetValue("suggested_waste", out var suggested) && suggested > 0 ? suggested : r.WastePercent;
            var raw = driver * (product?.TakeoffMultiplier ?? r.Multiplier) * (1 + waste / 100m);
            if (raw <= 0) continue;
            var qty = Round(raw, r.RoundMode, r.RoundTo);
            if (qty > 0 && qty < r.MinQty) qty = r.MinQty;
            if (r.MaxQty is { } max && qty > max) qty = max;
            if (qty <= 0) continue;
            result.Add(new TakeoffLine(r, product, driver, raw, qty, r.Description ?? product?.Name ?? r.Name, problem));
        }
        return result;
    }

    /// <summary>Placeholders every rule's wording can use, besides the trade's measurements by key.</summary>
    public static readonly (string Key, string Label)[] ScopeFields =
    [
        ("qty", "the quantity"), ("unit", "the product's unit"), ("product", "the product's name"),
        ("line", "the line description"), ("measure", "the measurement the rule is based on"),
    ];

    private static readonly System.Text.RegularExpressions.Regex Placeholder = new(@"\{([a-z0-9_]+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Placeholders in a wording template that aren't fields or measurement keys.</summary>
    public static List<string> UnknownPlaceholders(string template, IEnumerable<string> driverKeys)
    {
        var known = driverKeys.Concat(ScopeFields.Select(f => f.Key)).ToHashSet();
        return Placeholder.Matches(template).Select(m => m.Groups[1].Value).Where(k => !known.Contains(k)).Distinct().ToList();
    }

    /// <summary>
    /// A line's scope-of-work wording: the rule's template with {qty}, {unit}, {product}, {line},
    /// {measure} and any measurement key (e.g. {total_area_squares}) filled in. Null without wording.
    /// </summary>
    public static string? ScopeText(TakeoffLine line, IReadOnlyDictionary<string, decimal> drivers)
    {
        if (string.IsNullOrWhiteSpace(line.Rule.ScopeText)) return null;
        return Placeholder.Replace(line.Rule.ScopeText.Trim(), m => m.Groups[1].Value switch
        {
            "qty" => Num(line.Qty),
            "unit" => line.Product?.Unit ?? "",
            "product" => line.Product?.Name ?? line.Rule.Name,
            "line" => line.Description,
            "measure" => Num(line.Driver),
            var k => drivers.TryGetValue(k, out var v) ? Num(v) : m.Value,
        });
    }

    /// <summary>
    /// The section's scope of work from the lines that will be added (in rule order), one "- " item
    /// per rule with wording; identical wording is listed once. Null when no rule has wording.
    /// </summary>
    public static string? Scope(IEnumerable<TakeoffLine> lines, IReadOnlyDictionary<string, decimal> drivers)
    {
        var items = lines.Where(l => l.Problem is null && l.Product is not null)
            .Select(l => ScopeText(l, drivers)).OfType<string>().Distinct().ToList();
        return items.Count == 0 ? null : string.Join("\n", items.Select(i => "- " + i));
    }

    private static string Num(decimal d) => d.ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture);

    public static decimal Round(decimal qty, string mode, decimal to)
    {
        if (to <= 0) to = 1;
        var units = qty / to;
        var rounded = mode switch
        {
            TakeoffRounding.Up => Math.Ceiling(units - 0.000001m),
            TakeoffRounding.Down => Math.Floor(units + 0.000001m),
            TakeoffRounding.Nearest => Math.Round(units, MidpointRounding.AwayFromZero),
            _ => units,
        };
        return Math.Round(mode == TakeoffRounding.None ? qty : rounded * to, 3, MidpointRounding.AwayFromZero);
    }

    private static bool Holds(string? field, string? op, decimal? value, IReadOnlyDictionary<string, decimal> drivers)
    {
        if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(op) || value is not { } v) return true;
        var x = drivers.GetValueOrDefault(field);
        return op switch
        {
            "eq" => x == v, "neq" => x != v, "gt" => x > v, "gte" => x >= v, "lt" => x < v, "lte" => x <= v, _ => true,
        };
    }

    public static string Label(string key) => string.IsNullOrEmpty(key) ? "" : char.ToUpperInvariant(key[0]) + key[1..].Replace('_', ' ');
}
