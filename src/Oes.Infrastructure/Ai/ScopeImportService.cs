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
    decimal? CarrierUnitPrice, Guid? PriceListItemId, string? MatchedDescription, decimal? OurUnitPrice);

public sealed record ScopeImportView(ScopeImport Import, Estimate Estimate, List<ScopeLine> Lines);

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

    private async Task<Estimate> VisibleEditableAsync(Guid estimateId, CancellationToken ct)
    {
        var d = await estimates.GetAsync(estimateId, ct); // visibility
        if (!d.CanEdit) throw new RecordForbiddenException("You can't edit this estimate.");
        if (d.Estimate.Status == EstimateStatuses.Signed) throw new CrmRuleException("This estimate is signed. Use a change order instead.");
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
                description.Length <= 300 ? description : description[..300], Math.Round(q, 2), (S("unit") ?? "EA").ToUpperInvariant() is var u && u.Length <= 10 ? u : "EA",
                price, null, null, null));
        }
        return list;
    }

    /// <summary>Matches lines to the estimate's price list by category code + item code + activity.</summary>
    private async Task<List<ScopeLine>> MatchAsync(Estimate est, List<ScopeLine> lines, CancellationToken ct)
    {
        if (est.PriceListId is not { } listId) return lines;
        var codes = lines.Where(l => l.Code is not null).Select(l => l.Code!).Distinct().ToList();
        var candidates = await (from pli in db.PriceListItems where pli.PriceListId == listId
                                join ci in db.CatalogItems on pli.CatalogItemId equals ci.Id
                                join cat in db.CatalogCategories on ci.CategoryId equals cat.Id
                                where codes.Contains(ci.Code)
                                select new { pli.Id, Category = cat.Code, ci.Code, pli.Activity, ci.Description, pli.UnitPrice }).ToListAsync(ct);
        return lines.Select(l =>
        {
            var m = candidates.FirstOrDefault(c => c.Code == l.Code && (l.Category is null || c.Category == l.Category) && c.Activity == l.Activity)
                    ?? (l.Activity == Activities.None ? candidates.FirstOrDefault(c => c.Code == l.Code && (l.Category is null || c.Category == l.Category)) : null);
            return m is null ? l : l with { PriceListItemId = m.Id, MatchedDescription = m.Description, OurUnitPrice = m.UnitPrice };
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
    public async Task<int> ApplyAsync(Guid importId, IReadOnlyCollection<int> include, IReadOnlyDictionary<int, decimal> quantities, string sectionName, CancellationToken ct = default)
    {
        var view = await GetAsync(importId, ct);
        if (view.Import.Status != ScopeImportStatuses.Review) throw new CrmRuleException("This scope was already added or discarded.");
        await VisibleEditableAsync(view.Estimate.Id, ct);
        var chosen = view.Lines.Where(l => include.Contains(l.Index)).ToList();
        if (chosen.Count == 0) throw new CrmRuleException("Tick at least one line to add.");
        var name = string.IsNullOrWhiteSpace(sectionName) ? $"Carrier scope{(view.Import.Carrier is null ? "" : " – " + view.Import.Carrier)}" : sectionName.Trim();
        await estimates.AddSectionAsync(view.Estimate.Id, name.Length <= 150 ? name : name[..150], null, null, ct);
        var sectionId = await db.EstimateSections.Where(s => s.EstimateId == view.Estimate.Id).OrderByDescending(s => s.Sort).Select(s => s.Id).FirstAsync(ct);
        foreach (var l in chosen)
        {
            var qty = quantities.TryGetValue(l.Index, out var q) && q > 0 ? q : l.Quantity;
            if (l.PriceListItemId is { } pli) await estimates.AddPriceBookLineAsync(view.Estimate.Id, sectionId, pli, qty, ct);
            else await estimates.AddMiscLineAsync(view.Estimate.Id, sectionId,
                l.Code is null ? l.Description : $"{l.Description} ({l.Category} {l.Code})", qty, l.Unit, l.CarrierUnitPrice ?? 0, false, ct);
        }
        var import = await db.ScopeImports.FirstAsync(x => x.Id == importId, ct);
        import.Status = ScopeImportStatuses.Imported;
        import.ImportedAt = DateTime.UtcNow;
        import.ImportedByUserId = user.UserId;
        await items.LogAsync(EntityTypes.Estimate, view.Estimate.Id, "estimate.scope_imported", $"Added {chosen.Count} lines from {view.Import.FileName}", ct);
        await db.SaveChangesAsync(ct);
        return chosen.Count;
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
