using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Estimating;
using Oes.Infrastructure.Files;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Ai;

/// <summary>One line read from a carrier scope, with its match on the estimate's price list (if any).</summary>
public sealed record ScopeLine(
    int Index, string? Category, string? Code, string Activity, string Description, decimal Quantity, string Unit,
    decimal? CarrierUnitPrice, Guid? PriceListItemId, string? MatchedDescription, decimal? OurUnitPrice,
    string? MatchKind = null, string? MatchNote = null, Guid? SuggestedItemId = null, string? SuggestedDescription = null,
    decimal? SuggestedUnitPrice = null, string? SuggestedUnit = null)
{
    /// <summary>Matched by something other than the item code or exact wording, so worth a look.</summary>
    public bool NeedsCheck => MatchKind is ScopeMatchKinds.Close or ScopeMatchKinds.Shortened or ScopeMatchKinds.Activity;
}

public sealed record ScopeImportView(ScopeImport Import, Estimate Estimate, List<ScopeLine> Lines);

public enum CompareKind { Same, Different, CarrierOnly, OursOnly }

/// <summary>One item on the carrier scope and/or our estimate, quantities summed across lines and sections.</summary>
public sealed record CompareRow(
    string? Code, string Activity, string Description, string Unit,
    decimal? CarrierQty, decimal? CarrierUnitPrice, decimal CarrierAmount,
    decimal? OurQty, decimal? OurUnitPrice, decimal OurAmount)
{
    public decimal Difference => OurAmount - CarrierAmount;
    public CompareKind Kind => CarrierQty is null ? CompareKind.OursOnly : OurQty is null ? CompareKind.CarrierOnly
        : CarrierQty == OurQty && CarrierUnitPrice == OurUnitPrice ? CompareKind.Same : CompareKind.Different;
}

public sealed record SupplementCompare(ScopeImport Import, Estimate Estimate, List<CompareRow> Rows)
{
    public decimal CarrierTotal => Rows.Sum(r => r.CarrierAmount);
    public decimal OurTotal => Rows.Sum(r => r.OurAmount);
    public decimal Difference => OurTotal - CarrierTotal;
}

/// <summary>
/// Carrier scope import: Claude reads the scope PDF (Xactimate or similar) into lines with one structured-output
/// call; each line is matched to the estimate's price list by category, item code and activity. Nothing reaches
/// the estimate until a person reviews the lines and adds them. Matched lines use our price list; the rest come in
/// as custom lines at the carrier's price, which go through approval by exception like any custom line.
/// </summary>
public sealed class ScopeImportService(
    OesDbContext db, ICurrentUser user, TenantTime time, IAiClient ai, AiUsageMeter meter, RecordItems items,
    EstimateService estimates, ILogger<ScopeImportService> log)
{
    public const long MaxPdfBytes = 30 * 1024 * 1024;
    public const int MaxLines = 400;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] ActivityValues = [Activities.Replace, Activities.Remove, Activities.RemoveReplace, Activities.Install, Activities.Detach, "other"];

    public bool IsConfigured => ai.IsConfigured;

    private static JsonElement Schema() => JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "carrier", "price_list", "notes", "lines" },
        properties = new
        {
            carrier = new { type = new[] { "string", "null" }, description = "Insurance company named on the scope." },
            price_list = new { type = new[] { "string", "null" }, description = "Price list code printed on the scope, e.g. NENE8X_NOV26." },
            notes = new { type = new[] { "string", "null" }, description = "Anything the user should know (pages you couldn't read, depreciation shown separately, multiple structures)." },
            lines = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "category", "code", "activity", "description", "quantity", "unit", "unit_price" },
                    properties = new
                    {
                        category = new { type = new[] { "string", "null" }, description = "Category code, e.g. RFG." },
                        code = new { type = new[] { "string", "null" }, description = "Item code within the category, e.g. 300S." },
                        activity = new { type = "string", @enum = ActivityValues, description = "replace, remove (tear off), r_and_r (remove and replace), install, detach_reset, or other." },
                        description = new { type = "string" },
                        quantity = new { type = "number" },
                        unit = new { type = "string", description = "Unit as printed: SQ, LF, SF, EA…" },
                        unit_price = new { type = new[] { "number", "null" }, description = "Unit price (replacement cost), before tax." },
                    },
                },
            },
        },
    });

    /// <summary>Reading a scope and comparing need edit rights; adding lines also needs an unsigned estimate.</summary>
    private async Task<Estimate> VisibleEditableAsync(Guid estimateId, CancellationToken ct, bool toAddLines = false)
    {
        var d = await estimates.GetAsync(estimateId, ct); // visibility
        if (!await estimates.HasEditRightsAsync(d.Estimate)) throw new RecordForbiddenException("You can't edit this estimate.");
        if (toAddLines && d.Estimate.Status == EstimateStatuses.Signed)
            throw new CrmRuleException("This estimate is signed. Use the supplement compare, and a change order for extra work.");
        return d.Estimate;
    }

    public async Task<Guid> ExtractAsync(Guid estimateId, Stream pdf, string fileName, CancellationToken ct = default)
    {
        var est = await VisibleEditableAsync(estimateId, ct);
        var me = user.UserId ?? throw new RecordForbiddenException("Sign in first.");
        if (!ai.IsConfigured) throw new CrmRuleException("AI reading isn't connected yet. An administrator needs to add the AI API key.");
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new CrmRuleException("Attach the scope as a PDF.");
        var today = await time.TodayAsync();
        if (await meter.OverBudgetAsync(me, today, ct) is { } over) throw new CrmRuleException(over);

        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, ct);
        if (buffer.Length is 0 or > MaxPdfBytes) throw new CrmRuleException("Scopes up to 30 MB can be read.");
        var bytes = buffer.ToArray();
        if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F') throw new CrmRuleException("That file isn't a PDF.");

        buffer.Position = 0;
        Guid documentId;
        try { documentId = await items.UploadAsync(EntityTypes.Estimate, est.Id, buffer, fileName, "application/pdf", "carrier_scope", ct); }
        catch (FileRejectedException ex) { throw new CrmRuleException(ex.Message); }

        const string system = "You read insurance carrier scopes of loss (Xactimate, Symbility and similar) for a roofing contractor's estimating system. Copy what is printed; never invent lines or numbers.";
        const string instructions =
            "List every line item in this scope, in order, across all pages. Use the line's own category and item code when shown (e.g. RFG / 300S). " +
            "Quantities and units exactly as printed. Unit price is the replacement cost per unit before tax. Skip subtotal, tax, O&P, depreciation and summary rows.";
        var response = await ai.ExtractAsync(new AiExtractRequest(system, bytes, instructions, Schema(), MaxTokens: 32000), ct);
        await meter.RecordAsync(me, today, response.Usage, ct);
        await db.SaveChangesAsync(ct);
        if (response.Json is null)
            throw new CrmRuleException(response.StopReason == "refusal"
                ? "The AI couldn't read that scope. It's saved on the estimate; enter the lines by hand."
                : "The AI didn't finish reading that scope (it may be very long). It's saved on the estimate; try again or enter the lines by hand.");

        JsonElement root;
        try { root = JsonDocument.Parse(response.Json).RootElement; }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Scope import returned invalid JSON");
            throw new CrmRuleException("The AI's answer couldn't be read. Enter the lines by hand.");
        }

        var lines = await MatchAsync(est, ReadLines(root), ct);
        string? Str(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
        var import = new ScopeImport
        {
            EstimateId = est.Id, DocumentId = documentId, FileName = Cut(fileName, 255)!, Carrier = Cut(Str("carrier"), 150),
            CarrierPriceList = Cut(Str("price_list"), 60), Notes = Cut(Str("notes"), 1000), ExtractedJson = response.Json,
            LinesJson = JsonSerializer.Serialize(lines, Json),
        };
        db.ScopeImports.Add(import);
        await db.SaveChangesAsync(ct);
        return import.Id;
    }

    private static List<ScopeLine> ReadLines(JsonElement root)
    {
        var list = new List<ScopeLine>();
        if (!root.TryGetProperty("lines", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var l in arr.EnumerateArray())
        {
            if (list.Count >= MaxLines) break;
            string? S(string k) => l.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
            decimal? D(string k) => l.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : null;
            var description = S("description");
            var qty = D("quantity");
            if (string.IsNullOrEmpty(description) || qty is not { } q || q <= 0 || q > 1_000_000) continue;
            var price = D("unit_price") is { } p && p >= 0 && p < 1_000_000 ? Math.Round(p, 2) : (decimal?)null;
            var activity = S("activity") is { } a && Activities.All.Contains(a) ? a : Activities.None;
            list.Add(new ScopeLine(list.Count, S("category")?.ToUpperInvariant(), S("code")?.ToUpperInvariant(), activity,
                description.Length <= 300 ? description : description[..300], Math.Round(q, 2), (S("unit") ?? "").ToUpperInvariant() is var u && u.Length <= 10 ? u : "",
                price, null, null, null));
        }
        return list;
    }

    /// <summary>Matches lines to the estimate's price list (see <see cref="ScopeMatcher"/>).</summary>
    private async Task<List<ScopeLine>> MatchAsync(Estimate est, List<ScopeLine> lines, CancellationToken ct)
    {
        if (est.PriceListId is not { } listId) return lines;
        var book = await (from pli in db.PriceListItems where pli.PriceListId == listId
                          join ci in db.CatalogItems on pli.CatalogItemId equals ci.Id
                          join cat in db.CatalogCategories on ci.CategoryId equals cat.Id
                          where ci.IsActive
                          select new PriceBookEntry(pli.Id, cat.Code, ci.Code, pli.Activity, ci.Description, ci.Unit, pli.UnitPrice)).ToListAsync(ct);
        var matcher = new ScopeMatcher(book);
        return lines.Select(l =>
        {
            var m = matcher.Match(l.Category, l.Code, l.Activity, l.Description, l.Unit);
            return l with
            {
                PriceListItemId = m.Match?.Id, MatchedDescription = m.Match?.Description, OurUnitPrice = m.Match?.UnitPrice,
                MatchKind = m.Kind, MatchNote = m.Note, SuggestedItemId = m.Suggestion?.Id, SuggestedDescription = m.Suggestion?.Description,
                SuggestedUnitPrice = m.Suggestion?.UnitPrice, SuggestedUnit = m.Suggestion?.Unit,
            };
        }).ToList();
    }

    /// <summary>Earlier imports for this estimate, newest first.</summary>
    public async Task<List<ScopeImport>> ForEstimateAsync(Guid estimateId, CancellationToken ct = default)
    {
        await estimates.GetAsync(estimateId, ct); // visibility
        return await db.ScopeImports.Where(x => x.EstimateId == estimateId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    }

    public async Task<ScopeImportView> GetAsync(Guid importId, CancellationToken ct = default)
    {
        var import = await db.ScopeImports.FirstOrDefaultAsync(x => x.Id == importId, ct) ?? throw new RecordNotFoundException("Scope import");
        var est = (await estimates.GetAsync(import.EstimateId, ct)).Estimate; // visibility follows the estimate
        return new ScopeImportView(import, est, JsonSerializer.Deserialize<List<ScopeLine>>(import.LinesJson, Json) ?? []);
    }

    /// <summary>Adds the chosen lines (with any corrected quantities) to the estimate as a new section.</summary>
    public async Task<int> ApplyAsync(Guid importId, IReadOnlyCollection<int> include, IReadOnlyDictionary<int, decimal> quantities, string sectionName,
        IReadOnlyCollection<int>? useSuggestion = null, CancellationToken ct = default)
    {
        var view = await GetAsync(importId, ct);
        if (view.Import.Status != ScopeImportStatuses.Review) throw new CrmRuleException("This scope was already added or discarded.");
        await VisibleEditableAsync(view.Estimate.Id, ct, toAddLines: true);
        var chosen = view.Lines.Where(l => include.Contains(l.Index)).ToList();
        if (chosen.Count == 0) throw new CrmRuleException("Tick at least one line to add.");
        var name = string.IsNullOrWhiteSpace(sectionName) ? $"Carrier scope{(view.Import.Carrier is null ? "" : " – " + view.Import.Carrier)}" : sectionName.Trim();
        await estimates.AddSectionAsync(view.Estimate.Id, name.Length <= 150 ? name : name[..150], null, null, ct);
        var sectionId = await db.EstimateSections.Where(s => s.EstimateId == view.Estimate.Id).OrderByDescending(s => s.Sort).Select(s => s.Id).FirstAsync(ct);
        foreach (var l in chosen)
        {
            var qty = quantities.TryGetValue(l.Index, out var q) && q > 0 ? q : l.Quantity;
            var item = l.PriceListItemId ?? (useSuggestion?.Contains(l.Index) == true ? l.SuggestedItemId : null);
            if (item is { } pli) await estimates.AddPriceBookLineAsync(view.Estimate.Id, sectionId, pli, qty, ct);
            else await estimates.AddMiscLineAsync(view.Estimate.Id, sectionId,
                l.Code is null ? l.Description : $"{l.Description} ({l.Category} {l.Code})", qty, l.Unit is { Length: > 0 } u ? u : "EA", l.CarrierUnitPrice ?? 0, false, ct);
        }
        var import = await db.ScopeImports.FirstAsync(x => x.Id == importId, ct);
        import.Status = ScopeImportStatuses.Imported;
        import.ImportedAt = DateTime.UtcNow;
        import.ImportedByUserId = user.UserId;
        await items.LogAsync(EntityTypes.Estimate, view.Estimate.Id, "estimate.scope_imported", $"Added {chosen.Count} lines from {view.Import.FileName}", ct);
        await db.SaveChangesAsync(ct);
        return chosen.Count;
    }

    /// <summary>
    /// Supplement compare: the carrier's scope against the estimate as it stands now, item by item. Items pair up by
    /// category + item code + activity; lines without a code pair up by description. Amounts are before O&amp;P and tax.
    /// </summary>
    public async Task<SupplementCompare> CompareAsync(Guid importId, CancellationToken ct = default)
    {
        var view = await GetAsync(importId, ct);
        var ours = await db.EstimateLines.Where(l => l.EstimateId == view.Estimate.Id).OrderBy(l => l.Sort).ToListAsync(ct);

        static string Norm(string s) => string.Join(' ', s.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        static string CodeKey(string code, string activity) => "C|" + Norm(code) + "|" + activity;
        // A custom line made from a scope ends with "(CAT CODE)".
        static string? CodeFromDescription(string d) =>
            d.EndsWith(')') && d.LastIndexOf(" (", StringComparison.Ordinal) is var i and > 0 ? d[(i + 2)..^1] : null;

        var rows = new Dictionary<string, CompareAcc>();
        void Add(string key, string? code, string activity, string description, string unit, bool carrier, decimal qty, decimal amount, decimal? unitPrice)
        {
            if (!rows.TryGetValue(key, out var r)) rows[key] = r = new CompareAcc(code, activity, description, unit);
            (carrier ? r.Carrier : r.Ours).Add(qty, amount, unitPrice);
        }

        foreach (var l in view.Lines)
        {
            var code = l.Code is null ? null : l.Category is null ? l.Code : $"{l.Category} {l.Code}";
            var key = code is null ? "D|" + Norm(l.Description) : CodeKey(code, l.Activity);
            var amount = Math.Round(l.Quantity * (l.CarrierUnitPrice ?? 0), 2, MidpointRounding.AwayFromZero);
            Add(key, code, l.Activity, l.Description, l.Unit, true, l.Quantity, amount, l.CarrierUnitPrice);
        }
        foreach (var l in ours)
        {
            var code = l.Code ?? CodeFromDescription(l.Description);
            var description = l.Code is null && code is not null ? l.Description[..l.Description.LastIndexOf(" (", StringComparison.Ordinal)] : l.Description;
            string key;
            if (code is null) key = "D|" + Norm(l.Description);
            else if (rows.ContainsKey(CodeKey(code, l.Activity))) key = CodeKey(code, l.Activity);
            // A carrier line with the same code but no activity, or (for our custom lines, which have none) any activity, still pairs.
            else if (rows.ContainsKey(CodeKey(code, Activities.None))) key = CodeKey(code, Activities.None);
            else if (l.Activity == Activities.None && rows.Keys.FirstOrDefault(k => k.StartsWith(CodeKey(code, ""), StringComparison.Ordinal)) is { } any) key = any;
            else key = CodeKey(code, l.Activity);
            Add(key, code, l.Activity, description, l.Unit, false, l.Qty, l.Amount, l.UnitPrice);
        }

        var list = rows.Values.Select(r => new CompareRow(r.Code, r.Activity, r.Description, r.Unit,
                r.Carrier.Any ? r.Carrier.Qty : null, r.Carrier.Price, r.Carrier.Amount, r.Ours.Any ? r.Ours.Qty : null, r.Ours.Price, r.Ours.Amount))
            .OrderBy(r => r.Kind == CompareKind.Same ? 1 : 0).ThenBy(r => r.Code ?? "~").ThenBy(r => r.Description).ToList();
        return new SupplementCompare(view.Import, view.Estimate, list);
    }

    public async Task DiscardAsync(Guid importId, CancellationToken ct = default)
    {
        var view = await GetAsync(importId, ct);
        await VisibleEditableAsync(view.Estimate.Id, ct);
        var import = await db.ScopeImports.FirstAsync(x => x.Id == importId, ct);
        if (import.Status == ScopeImportStatuses.Review) import.Status = ScopeImportStatuses.Discarded;
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class CompareAcc(string? code, string activity, string description, string unit)
{
    public string? Code { get; } = code;
    public string Activity { get; } = activity;
    public string Description { get; } = description;
    public string Unit { get; } = unit;
    public CompareSide Carrier { get; } = new();
    public CompareSide Ours { get; } = new();
}

internal sealed class CompareSide
{
    public bool Any { get; private set; }
    public decimal Qty { get; private set; }
    public decimal Amount { get; private set; }
    /// <summary>The unit price when every line agrees on one; otherwise null.</summary>
    public decimal? Price { get; private set; }

    public void Add(decimal qty, decimal amount, decimal? unitPrice)
    {
        Price = Any && Price != unitPrice ? null : unitPrice;
        Any = true;
        Qty += qty;
        Amount += amount;
    }
}
