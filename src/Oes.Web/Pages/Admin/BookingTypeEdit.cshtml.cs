using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Scheduling;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Admin;

[HasPermission(Permissions.BookingManage)]
public class BookingTypeEditModel(BookingService booking, CrmLookups lookups) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }
    [BindProperty] public BookingTypeInput Input { get; set; } = new();
    public List<Option> Users { get; private set; } = [];
    public List<Option> AppointmentTypes { get; private set; } = [];

    private async Task LoadAsync()
    {
        Users = await lookups.UsersAsync();
        AppointmentTypes = await lookups.ValuesAsync(Oes.Domain.Work.AppointmentTypes.LookupType);
    }

    public Task<IActionResult> OnGetAsync() => Guard(async () =>
    {
        if (Id is { } id)
        {
            var t = await booking.GetTypeAsync(id);
            Input = new BookingTypeInput
            {
                Name = t.Name, Slug = t.Slug, Description = t.Description, DurationMinutes = t.DurationMinutes, SlotIntervalMinutes = t.SlotIntervalMinutes,
                BufferBeforeMinutes = t.BufferBeforeMinutes, BufferAfterMinutes = t.BufferAfterMinutes, MinNoticeMinutes = t.MinNoticeMinutes,
                DaysAhead = t.DaysAhead, DailyCapPerHost = t.DailyCapPerHost, AppointmentTypeKey = t.AppointmentTypeKey, AsksAddress = t.AsksAddress,
                CreatesOpportunity = t.CreatesOpportunity, IsActive = t.IsActive, HostUserIds = t.Hosts.Select(h => h.UserId).ToList(),
            };
        }
        await LoadAsync();
        return Page();
    });

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        return await Guard(async () =>
        {
            await booking.SaveTypeAsync(Id, Input);
            Flash("Booking type saved.");
            return RedirectToPage("/Admin/BookingTypes");
        }, Page);
    }
}
