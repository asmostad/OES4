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
    public ManageView? View { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        View = await booking.ManageViewAsync(Token);
        if (View is null) Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
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
