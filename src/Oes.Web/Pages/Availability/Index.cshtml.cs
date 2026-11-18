using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Scheduling;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Availability;

/// <summary>Weekly hours for online booking. Everyone can set their own; booking.manage can set anyone's.</summary>
public class IndexModel(BookingService booking, CrmLookups lookups, ICurrentUser user) : CrmPageModel
{
    public static readonly DayOfWeek[] Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    [BindProperty(SupportsGet = true, Name = "user")] public Guid? UserId { get; set; }
    /// <summary>Two windows per day: Start[day*2 + n], End[day*2 + n]. Blank = off.</summary>
    [BindProperty] public TimeOnly?[] Start { get; set; } = new TimeOnly?[14];
    [BindProperty] public TimeOnly?[] End { get; set; } = new TimeOnly?[14];

    public bool CanManage { get; private set; }
    public List<Option> Users { get; private set; } = [];
    public Guid Target => UserId ?? user.UserId ?? Guid.Empty;

    private async Task LoadAsync()
    {
        CanManage = await user.HasPermissionAsync(Permissions.BookingManage);
        if (CanManage) Users = await lookups.UsersAsync();
    }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        await LoadAsync();
        var rules = await booking.HoursAsync(Target);
        Start = new TimeOnly?[14]; End = new TimeOnly?[14];
        for (var d = 0; d < 7; d++)
        {
            var mine = rules.Where(r => r.DayOfWeek == Days[d]).OrderBy(r => r.StartTime).Take(2).ToList();
            for (var n = 0; n < mine.Count; n++) { Start[d * 2 + n] = mine[n].StartTime; End[d * 2 + n] = mine[n].EndTime; }
        }
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        return await Guard(async () =>
        {
            var hours = new List<(DayOfWeek, TimeOnly, TimeOnly)>();
            for (var i = 0; i < 14; i++)
            {
                var (s, e) = (Start.ElementAtOrDefault(i), End.ElementAtOrDefault(i));
                if (s is null && e is null) continue;
                if (s is null || e is null) throw new CrmRuleException($"{Days[i / 2]}: enter both a start and an end time.");
                hours.Add((Days[i / 2], s.Value, e.Value));
            }
            await booking.SaveHoursAsync(Target, hours);
            Flash("Hours saved.");
            return RedirectToPage(new { user = UserId });
        }, Page);
    }
}
