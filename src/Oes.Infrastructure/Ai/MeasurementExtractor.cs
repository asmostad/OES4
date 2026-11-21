using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Files;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Ai;

/// <summary>What the AI read from a report: proposed values for a person to check before anything is saved.</summary>
public sealed record MeasurementExtraction(
    Guid DocumentId, string FileName, Dictionary<string, decimal> Values, string? Source, DateOnly? MeasuredOn, string? Notes, string RawJson);

/// <summary>
/// Reads a measurement report (EagleView, Hover, GAF QuickMeasure, Roofr…) into one trade's measurement
/// fields with a single structured-output call. The PDF is stored on the property first; the result only
/// pre-fills the entry form. A person checks every number and saves the version.
/// </summary>
public sealed class MeasurementExtractor(
    OesDbContext db, RecordScope scope, ICurrentUser user, TenantTime time, IAiClient ai, AiUsageMeter meter, RecordItems items,
    ILogger<MeasurementExtractor> log)
{
    public const long MaxPdfBytes = 30 * 1024 * 1024;
    private static readonly string[] Vendors = ["eagleview", "hover", "gaf_quickmeasure", "roofr", "xactimate", "other"];

    public bool IsConfigured => ai.IsConfigured;

    /// <summary>"total_area" for whole-structure fields, "front__wall_area" for elevation fields.</summary>
    private static string SchemaKey(string surface, string field) => surface.Length == 0 ? field : $"{surface}__{field}";

    public async Task<MeasurementExtraction> ExtractAsync(Guid propertyId, Guid structureId, Guid tradeTypeId, Stream pdf, string fileName, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.MeasurementsManage);
        await scope.EnsureCanSeeAsync(EntityTypes.Property, propertyId);
        var me = user.UserId ?? throw new RecordForbiddenException("Sign in first.");
        if (!ai.IsConfigured) throw new CrmRuleException("AI reading isn't connected yet. An administrator needs to add the AI API key.");
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new CrmRuleException("Attach the report as a PDF.");
        var today = await time.TodayAsync();
        if (await meter.OverBudgetAsync(me, today, ct) is { } over) throw new CrmRuleException(over);

        var structure = await db.PropertyStructures.FirstOrDefaultAsync(s => s.Id == structureId && s.PropertyId == propertyId, ct)
                        ?? throw new CrmRuleException("That structure isn't on this property.");
        var trade = await db.TradeTypes.FirstOrDefaultAsync(t => t.Id == tradeTypeId && t.IsActive, ct) ?? throw new CrmRuleException("Unknown trade.");
        var fields = await db.MeasurementFieldDefs.Where(f => f.TradeTypeId == trade.Id && f.IsActive).OrderBy(f => f.Sort).ToListAsync(ct);
        if (fields.Count == 0 || trade.MeasurementMode == MeasurementModes.None) throw new CrmRuleException($"{trade.Name} doesn't take measurements.");
        var surfaces = MeasurementModes.Surfaces(trade.MeasurementMode);

        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, ct);
        if (buffer.Length == 0) throw new CrmRuleException("The file is empty.");
        if (buffer.Length > MaxPdfBytes) throw new CrmRuleException("Reports up to 30 MB can be read.");
        var bytes = buffer.ToArray();
        if (bytes.Length < 5 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F') throw new CrmRuleException("That file isn't a PDF.");

        // Keep the report on the property whatever the AI makes of it.
        Guid documentId;
        buffer.Position = 0;
        try { documentId = await items.UploadAsync(EntityTypes.Property, propertyId, buffer, fileName, "application/pdf", "measurement_report", ct); }
        catch (FileRejectedException ex) { throw new CrmRuleException(ex.Message); }

        // Schema: every field is a number or null (never invented), plus where it came from.
        var props = new Dictionary<string, object>
        {
            ["source"] = new { type = "string", @enum = Vendors, description = "Who produced the report." },
            ["measured_on"] = new { type = new[] { "string", "null" }, description = "Report date as yyyy-MM-dd, or null." },
            ["notes"] = new { type = new[] { "string", "null" }, description = "Anything the user should know: other structures in the report, values you couldn't find, unit conversions you made." },
        };
        foreach (var s in surfaces)
            foreach (var f in fields)
                props[SchemaKey(s, f.Key)] = new { type = new[] { "number", "null" }, description = $"{(s.Length > 0 ? s + " elevation: " : "")}{f.Label} in {f.Unit}" };
        var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = props, required = props.Keys.ToArray(), additionalProperties = false });

        var fieldList = string.Join("\n", surfaces.SelectMany(s => fields.Select(f => $"- {SchemaKey(s, f.Key)}: {(s.Length > 0 ? s + " elevation, " : "")}{f.Label} ({f.Unit})")));
        var instructions =
            $"Read this measurement report for the {trade.Name.ToLowerInvariant()} on \"{structure.Name}\" and fill in these fields:\n{fieldList}\n\n" +
            "Use only numbers printed in the report, converted to the unit given (for example 10 squares = 1,000 sq ft; a square is 100 sq ft). " +
            "Use null for anything the report doesn't give; never estimate. If the report covers several buildings, use the main one and say so in notes.";
        const string system = "You read roofing and exterior measurement reports (EagleView, Hover, GAF QuickMeasure, Roofr and similar) for a contractor's CRM. Accuracy matters more than completeness: a wrong number becomes a wrong estimate.";

        var response = await ai.ExtractAsync(new AiExtractRequest(system, bytes, instructions, schema), ct);
        await meter.RecordAsync(me, today, response.Usage, ct);
        await db.SaveChangesAsync(ct);
        if (response.Json is null)
            throw new CrmRuleException(response.StopReason == "refusal"
                ? "The AI couldn't read that report. Enter the measurements by hand; the report is saved on the property."
                : "The AI didn't finish reading that report. Try again, or enter the measurements by hand.");

        JsonElement root;
        try { root = JsonDocument.Parse(response.Json).RootElement; }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Measurement extraction returned invalid JSON");
            throw new CrmRuleException("The AI's answer couldn't be read. Enter the measurements by hand.");
        }

        var values = new Dictionary<string, decimal>();
        foreach (var s in surfaces)
            foreach (var f in fields)
                if (root.TryGetProperty(SchemaKey(s, f.Key), out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) && d >= 0 && d <= 10_000_000)
                    values[$"{s}|{f.Key}"] = Math.Round(d, 2);
        string? Str(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var source = Str("source") is { } src && MeasurementSources.All.Contains(src) ? src : "other";
        var measuredOn = DateOnly.TryParseExact(Str("measured_on"), "yyyy-MM-dd", out var date) ? date : (DateOnly?)null;
        var notes = Str("notes") is { Length: > 0 } n ? (n.Length <= 1000 ? n : n[..1000]) : null;
        return new MeasurementExtraction(documentId, fileName, values, source, measuredOn, notes, response.Json);
    }
}
