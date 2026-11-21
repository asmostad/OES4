using Microsoft.AspNetCore.Mvc;
using Oes.Infrastructure.Insurance;

namespace Oes.Web.Controllers.Api;

public sealed record StormDto(Guid Id, string Name, DateOnly EventDate, string PerilKey, decimal? MaxHailSizeIn, int? MaxWindMph, int ZipCount, int Opportunities);
/// <summary>A door on the canvass list. Distance is in miles from where the phone is, when it sent its position and the property has one.</summary>
public sealed record CanvassDoor(Guid PropertyId, string Line1, string? Line2, string CityLine, string PostalCode, decimal? Latitude, decimal? Longitude, double? Miles);

/// <summary>
/// Storm canvassing from the field app: recent storms, then the properties in a storm's ZIP codes that have no lead on
/// it yet, nearest first when the phone shares its position. A lead taken at the door (POST mobile/leads with the
/// property and storm) takes the house off the list.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class StormsApiController(StormService storms) : ControllerBase
{
    [HttpGet("storms")]
    public async Task<List<StormDto>> List(CancellationToken ct) =>
        (await storms.ListAsync(false, ct)).Select(s => new StormDto(s.Id, s.Name, s.EventDate, s.PerilKey, s.MaxHailSizeIn, s.MaxWindMph, s.ZipCount,
            s.Opportunities)).ToList();

    [HttpGet("storms/{id:guid}/canvass")]
    public async Task<List<CanvassDoor>> Canvass(Guid id, double? lat, double? lng, int take = 200, CancellationToken ct = default)
    {
        // Sorting by distance needs the whole list, so read a wide page and sort here.
        var props = await storms.UntouchedAsync(id, lat is null || lng is null ? Math.Clamp(take, 1, 500) : 2000, ct);
        var doors = props.Select(p => new CanvassDoor(p.Id, p.Line1, p.Line2, p.CityLine, p.PostalCode, p.Latitude, p.Longitude,
            lat is { } la && lng is { } ln && p.Latitude is { } pla && p.Longitude is { } pln ? Math.Round(Miles(la, ln, (double)pla, (double)pln), 2) : null));
        if (lat is not null && lng is not null) doors = doors.OrderBy(d => d.Miles ?? double.MaxValue);
        return doors.Take(Math.Clamp(take, 1, 500)).ToList();
    }

    /// <summary>Great-circle distance in miles (haversine).</summary>
    public static double Miles(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var a = Math.Pow(Math.Sin(Rad(lat2 - lat1) / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(Rad(lng2 - lng1) / 2), 2);
        return 3958.8 * 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
