using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Scheduling;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Book;

/// <summary>Public: pick a day and time, then fill in your details. Works without JavaScript.</summary>
[EnableRateLimiting("portal")]
public class TypeModel(BookingService booking) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string Company { get; set; } = "";
    [BindProperty(SupportsGet = true, Name = "type")] public string TypeSlug { get; set; } = "";
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public TimeOnly? Time { get; set; }
    [BindProperty] public BookingRequest Input { get; set; } = new();

    public PublicCompany Info { get; private set; } = null!;
    public PublicBookingType Type { get; private set; } = null!;
    public SortedDictionary<DateOnly, List<TimeOnly>> Slots { get; private set; } = [];
    public bool Picked => Date is { } d && Time is { } t && Slots.TryGetValue(d, out var times) && times.Contains(t);

    private async Task<bool> LoadAsync()
    {
        if (await booking.OpenCompanyAsync(Company) is not { } info) return false;
        if (await booking.PublicTypeInfoAsync(TypeSlug) is not { } type) return false;
        Info = info; Type = type;
        Slots = await booking.SlotsAsync(TypeSlug);
        return true;
    }

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return NotFound();
        if (Date is null || Time is null) return Page();
        Input.Date = Date.Value;
        Input.Time = Time.Value;
        try
        {
            var r = await booking.BookAsync(TypeSlug, Input, HttpContext.Connection.RemoteIpAddress?.ToString());
            return Redirect($"/portal/b/{r.ManageToken}?booked=true");
        }
        catch (CrmRuleException ex)
        {
            ModelState.AddModelError("", ex.Message);
            Slots = await booking.SlotsAsync(TypeSlug);
            return Page();
        }
    }
}
