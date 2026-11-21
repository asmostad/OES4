using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Scheduling;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Portal;

/// <summary>The customer's link to their online booking: see it, cancel it.</summary>
[EnableRateLimiting("portal")]
public class BookingModel(BookingService booking) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    [BindProperty(SupportsGet = true)] public bool Booked { get; set; }
    [BindProperty(SupportsGet = true)] public bool Moved { get; set; }
    [BindProperty(SupportsGet = true)] public bool Reschedule { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public TimeOnly? Time { get; set; }
    public ManageView? View { get; private set; }
    public SortedDictionary<DateOnly, List<TimeOnly>> Slots { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        View = await booking.ManageViewAsync(Token);
        if (View is null) Response.StatusCode = StatusCodes.Status404NotFound;
        else if (Reschedule && View.CanReschedule) Slots = await booking.RescheduleSlotsAsync(Token);
        return Page();
    }

    public async Task<IActionResult> OnPostRescheduleAsync()
    {
        if (Date is null || Time is null) return RedirectToPage(new { token = Token, reschedule = true });
        try
        {
            await booking.RescheduleByCustomerAsync(Token, Date.Value, Time.Value);
            return RedirectToPage(new { token = Token, moved = true });
        }
        catch (RecordNotFoundException) { return await OnGetAsync(); }
        catch (CrmRuleException ex)
        {
            Error = ex.Message;
            Reschedule = true;
            Date = null; Time = null;
            return await OnGetAsync();
        }
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        try
        {
            if (!await booking.CancelByCustomerAsync(Token)) return await OnGetAsync();
            return RedirectToPage(new { token = Token });
        }
        catch (CrmRuleException ex)
        {
            Error = ex.Message;
            return await OnGetAsync();
        }
    }
}
