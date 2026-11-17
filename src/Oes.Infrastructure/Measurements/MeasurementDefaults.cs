using Microsoft.EntityFrameworkCore;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Measurements;

/// <summary>
/// Default trades (OES3's trade categories) and measurement fields. Idempotent per tenant:
/// adds only what's missing, matched by trade Code and field Key.
/// </summary>
public static class MeasurementDefaults
{
    public sealed record Field(string Key, string Label, string Unit, string? Group = null);

    public sealed record Trade(string Code, string Name, string Mode, string Color, string Icon, Field[] Fields);

    public static readonly Trade[] Trades =
    [
        new("roofing", "Roofing", MeasurementModes.Roof, "#dc2626", "bi-house",
        [
            new("total_area", "Total roof area", "sq ft", "Area"),
            new("pitched_area", "Pitched area", "sq ft", "Area"),
            new("flat_area", "Flat / low-slope area", "sq ft", "Area"),
            new("predominant_pitch", "Predominant pitch", "/12", "Area"),
            new("facets", "Facets", "ea", "Area"),
            new("layers", "Existing layers", "ea", "Area"),
            new("ridges", "Ridges", "LF", "Edges"),
            new("hips", "Hips", "LF", "Edges"),
            new("valleys", "Valleys", "LF", "Edges"),
            new("rakes", "Rakes", "LF", "Edges"),
            new("eaves", "Eaves", "LF", "Edges"),
            new("step_flashing", "Step flashing", "LF", "Flashing"),
            new("wall_flashing", "Wall / headwall flashing", "LF", "Flashing"),
            new("pipe_jacks", "Pipe jacks", "ea", "Penetrations"),
            new("vents", "Box / turtle vents", "ea", "Penetrations"),
            new("skylights", "Skylights", "ea", "Penetrations"),
            new("chimneys", "Chimneys", "ea", "Penetrations"),
            new("suggested_waste", "Suggested waste", "%", "Area"),
        ]),
        new("gutters", "Gutters", MeasurementModes.Linear, "#d97706", "bi-droplet",
        [
            new("gutter_length", "Gutter", "LF"),
            new("downspout_length", "Downspout", "LF"),
            new("downspouts", "Downspouts", "ea"),
            new("inside_miters", "Inside miters", "ea"),
            new("outside_miters", "Outside miters", "ea"),
            new("end_caps", "End caps", "ea"),
            new("gutter_guard", "Gutter guard", "LF"),
        ]),
        new("siding", "Siding", MeasurementModes.Elevation, "#059669", "bi-bricks",
        [
            new("wall_area", "Wall area", "sq ft"),
            new("openings_area", "Openings area", "sq ft"),
            new("windows", "Windows", "ea"),
            new("doors", "Doors", "ea"),
            new("outside_corners", "Outside corners", "LF"),
            new("inside_corners", "Inside corners", "LF"),
            new("starter", "Starter", "LF"),
            new("j_channel", "J-channel", "LF"),
            new("soffit", "Soffit", "sq ft"),
            new("fascia", "Fascia", "LF"),
        ]),
        new("windows", "Windows", MeasurementModes.Linear, "#2563eb", "bi-window",
        [
            new("windows", "Windows", "ea"),
            new("double_windows", "Double / mulled windows", "ea"),
            new("patio_doors", "Patio doors", "ea"),
            new("united_inches", "Total united inches", "UI"),
        ]),
        new("paint", "Paint", MeasurementModes.Elevation, "#7c3aed", "bi-brush",
        [
            new("wall_area", "Wall area", "sq ft"),
            new("trim", "Trim", "LF"),
            new("doors", "Doors", "ea"),
        ]),
        new("fencing", "Fencing", MeasurementModes.Linear, "#4b5563", "bi-border-outer",
        [
            new("fence_length", "Fence", "LF"),
            new("gates", "Gates", "ea"),
            new("height", "Height", "ft"),
        ]),
        new("general", "General", MeasurementModes.None, "#475569", "bi-tools", []),
    ];

    public static async Task EnsureAsync(OesDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var filter = new[] { OesDbContext.TenantFilter };
        var trades = await db.TradeTypes.IgnoreQueryFilters(filter).Where(t => t.TenantId == tenantId).ToListAsync(ct);
        var fields = await db.MeasurementFieldDefs.IgnoreQueryFilters(filter).Where(f => f.TenantId == tenantId)
            .Select(f => new { f.TradeTypeId, f.Key }).ToListAsync(ct);

        var sort = 0;
        foreach (var t in Trades)
        {
            sort += 10;
            var trade = trades.FirstOrDefault(x => x.Code == t.Code);
            if (trade is null)
            {
                trade = new TradeType { TenantId = tenantId, Code = t.Code, Name = t.Name, MeasurementMode = t.Mode, Color = t.Color, Icon = t.Icon, Sort = sort };
                db.TradeTypes.Add(trade);
            }
            var fieldSort = 0;
            foreach (var f in t.Fields)
            {
                fieldSort += 10;
                if (fields.Any(x => x.TradeTypeId == trade.Id && x.Key == f.Key)) continue;
                db.MeasurementFieldDefs.Add(new MeasurementFieldDef
                {
                    TenantId = tenantId, TradeTypeId = trade.Id, Key = f.Key, Label = f.Label, Unit = f.Unit, Group = f.Group, Sort = fieldSort,
                });
            }
        }
    }
}
