using Microsoft.AspNetCore.Mvc;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Measurements;
using Oes.Web.Infrastructure;

namespace Oes.Web.Controllers.Api;

public sealed record MeasureFieldDto(string Key, string Label, string Unit, string? Group);
public sealed record MeasureTradeDto(Guid Id, string Name, string Color, string Mode, List<string> Surfaces, List<MeasureFieldDto> Fields);
public sealed record StructureDto(Guid Id, string Name);
public sealed record SetSummaryDto(Guid Id, Guid StructureId, string StructureName, Guid TradeTypeId, string TradeName, string TradeColor, int VersionNo,
    int VersionCount, string Source, string SourceLabel, DateOnly? MeasuredOn, string? MeasuredBy, string Headline);
public sealed record PropertyMeasurementsDto(Guid PropertyId, string Address, List<StructureDto> Structures, List<MeasureTradeDto> Trades,
    List<SetSummaryDto> Current, bool CanMeasure);
/// <summary>Values keyed "surface|field", as the save takes them ("|total_area", "front|wall_area").</summary>
public sealed record SetVersionDto(Guid Id, int VersionNo, bool IsCurrent, string SourceLabel, DateOnly? MeasuredOn);
public sealed record MeasurementSetDto(Guid Id, Guid PropertyId, Guid StructureId, string StructureName, Guid TradeTypeId, string TradeName, string TradeColor,
    string Mode, int VersionNo, bool IsCurrent, string Source, string SourceLabel, DateOnly? MeasuredOn, string? MeasuredBy, string? Notes,
    List<string> Surfaces, List<MeasureFieldDto> Fields, Dictionary<string, decimal> Values, Dictionary<string, decimal> Previous,
    Guid? ReportDocumentId, string? ReportFileName, List<SetVersionDto> Versions, bool CanEdit);
public sealed record NewMeasurementsInput(Guid StructureId, Guid TradeTypeId, string? Source, DateOnly? MeasuredOn, string? Notes, Dictionary<string, decimal?> Values);
public sealed record SavedSetDto(Guid SetId);
public sealed record NewStructureInput(string Name);

/// <summary>
/// Measurements from the field app: what's on file for a property, one version in full, and a new version taken on
/// site (tape or manual; each save is a new version, never an edit). Reading a report with AI stays on the web.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class MeasurementsApiController(MeasurementService measurements, PropertyService properties) : ControllerBase
{
    private static string Key(MeasurementValue v) => $"{v.SurfaceKey}|{v.FieldKey}";

    [HttpGet("properties/{id:guid}/measurements")]
    public async Task<PropertyMeasurementsDto> ForProperty(Guid id, CancellationToken ct)
    {
        var m = await measurements.ForPropertyAsync(id, ct);
        var trades = new List<MeasureTradeDto>();
        foreach (var t in m.Trades)
            trades.Add(new MeasureTradeDto(t.Id, t.Name, t.Color, t.MeasurementMode, [.. MeasurementModes.Surfaces(t.MeasurementMode)],
                (await measurements.FieldsAsync(t.Id, true, ct)).Select(f => new MeasureFieldDto(f.Key, f.Label, f.Unit, f.Group)).ToList()));
        return new PropertyMeasurementsDto(id, m.Property.SingleLine, m.Structures.Select(s => new StructureDto(s.Id, s.Name)).ToList(), trades,
            m.Current.Select(s => new SetSummaryDto(s.Id, s.StructureId, s.StructureName, s.TradeTypeId, s.TradeName, s.TradeColor, s.VersionNo, s.VersionCount,
                s.Source, MeasurementService.SourceLabel(s.Source), s.MeasuredOn, s.MeasuredBy, s.Headline)).ToList(),
            await measurements.CanMeasureAsync());
    }

    [HttpGet("measurement-sets/{id:guid}")]
    public async Task<MeasurementSetDto> Set(Guid id, CancellationToken ct)
    {
        var d = await measurements.GetSetAsync(id, ct);
        var s = d.Set;
        return new MeasurementSetDto(s.Id, s.PropertyId, s.StructureId, d.Structure.Name, d.Trade.Id, d.Trade.Name, d.Trade.Color, d.Trade.MeasurementMode,
            s.VersionNo, s.IsCurrent, s.Source, MeasurementService.SourceLabel(s.Source), s.MeasuredOn, d.MeasuredBy, s.Notes,
            [.. MeasurementModes.Surfaces(d.Trade.MeasurementMode)], d.Fields.Select(f => new MeasureFieldDto(f.Key, f.Label, f.Unit, f.Group)).ToList(),
            s.Values.GroupBy(Key).ToDictionary(g => g.Key, g => g.First().Value),
            d.Previous?.Values.GroupBy(Key).ToDictionary(g => g.Key, g => g.First().Value) ?? [],
            d.SourceDocument?.Id, d.SourceDocument?.FileName,
            d.Versions.Select(v => new SetVersionDto(v.Id, v.VersionNo, v.IsCurrent, MeasurementService.SourceLabel(v.Source), v.MeasuredOn)).ToList(), d.CanEdit);
    }

    /// <summary>A new version for a structure and trade (the current one stays as history).</summary>
    [HttpPost("properties/{id:guid}/measurements")]
    [Idempotent]
    public async Task<SavedSetDto> Save(Guid id, NewMeasurementsInput input, CancellationToken ct) =>
        new(await measurements.SaveVersionAsync(id, input.StructureId, input.TradeTypeId, new MeasurementInput
        {
            Source = string.IsNullOrWhiteSpace(input.Source) ? "field_tape" : input.Source, MeasuredOn = input.MeasuredOn, Notes = input.Notes,
            Values = input.Values ?? [],
        }, null, ct));

    /// <summary>Another building on the property (detached garage, shed) to measure separately.</summary>
    [HttpPost("properties/{id:guid}/structures")]
    [Idempotent]
    public async Task<IActionResult> AddStructure(Guid id, NewStructureInput input, CancellationToken ct)
    {
        await properties.AddStructureAsync(id, input.Name ?? "", ct);
        return NoContent();
    }
}
