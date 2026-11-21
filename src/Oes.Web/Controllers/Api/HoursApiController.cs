using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Scheduling;

namespace Oes.Web.Controllers.Api;

/// <summary>A window of the week: day 0 = Sunday … 6 = Saturday, times as "HH:mm" in the company's time zone.</summary>
public sealed record HoursWindow(int Day, string Start, string End);
public sealed record HoursInput(List<HoursWindow> Hours);

/// <summary>Your own weekly hours for online booking, from the field app (the web also sets other people's).</summary>
[ApiController]
[Route("api/v1/me/hours")]
public sealed class HoursApiController(BookingService booking, ICurrentUser user) : ControllerBase
{
    private Guid Me => user.UserId ?? throw new RecordForbiddenException("Sign in first.");

    [HttpGet]
    public async Task<List<HoursWindow>> Get(CancellationToken ct) =>
        (await booking.HoursAsync(Me, ct)).Select(r => new HoursWindow((int)r.DayOfWeek, r.StartTime.ToString("HH:mm"), r.EndTime.ToString("HH:mm"))).ToList();

    /// <summary>Replaces the whole week (a day with no windows is off).</summary>
    [HttpPut]
    public async Task<IActionResult> Save(HoursInput input, CancellationToken ct)
    {
        var hours = new List<(DayOfWeek, TimeOnly, TimeOnly)>();
        foreach (var w in input.Hours ?? [])
        {
            if (w.Day is < 0 or > 6) throw new CrmRuleException("Unknown day.");
            if (!TimeOnly.TryParse(w.Start, System.Globalization.CultureInfo.InvariantCulture, out var s)
                || !TimeOnly.TryParse(w.End, System.Globalization.CultureInfo.InvariantCulture, out var e))
                throw new CrmRuleException($"{(DayOfWeek)w.Day}: enter both a start and an end time.");
            hours.Add(((DayOfWeek)w.Day, s, e));
        }
        await booking.SaveHoursAsync(Me, hours, ct);
        return NoContent();
    }
}
