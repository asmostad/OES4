using Microsoft.AspNetCore.Mvc;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Work;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Calendar;

/// <summary>Week agenda in the company's time zone. M365 calendar sync plugs in later.</summary>
public class IndexModel(WorkService work, CrmLookups lookups, TenantTime time, ICurrentUser user) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true, Name = "user")] public Guid? UserFilter { get; set; }

    public DateOnly WeekStart { get; private set; }
    public DateOnly Today { get; private set; }
    public List<AppointmentRow> Appointments { get; private set; } = [];
    public List<TaskRow> DueTasks { get; private set; } = [];
    public List<Option> Users { get; private set; } = [];
    public List<Option> Types { get; private set; } = [];
    public Guid? Me => user.UserId;

    public async Task OnGetAsync()
    {
        Today = await time.TodayAsync();
        var d = Date ?? Today;
        WeekStart = d.AddDays(-(int)d.DayOfWeek); // Sunday
        Appointments = await work.AppointmentsAsync(WeekStart, WeekStart.AddDays(6), UserFilter);
        var mine = await work.TasksAsync(onlyMine: true, includeDone: false);
        DueTasks = mine.Where(t => t.DueLocal is { } l && DateOnly.FromDateTime(l) >= WeekStart && DateOnly.FromDateTime(l) <= WeekStart.AddDays(6)).ToList();
        Users = await lookups.UsersAsync();
        Types = await lookups.ValuesAsync(Oes.Domain.Work.AppointmentTypes.LookupType);
    }
}
