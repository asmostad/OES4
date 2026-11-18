using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Oes.Infrastructure.Scheduling;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Book;

/// <summary>Public: the company's bookable appointment types.</summary>
[EnableRateLimiting("portal")]
public class CompanyModel(BookingService booking) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string Company { get; set; } = "";
    public PublicCompany? Info { get; private set; }
    public List<PublicBookingType> Types { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        Info = await booking.OpenCompanyAsync(Company);
        if (Info is null) return NotFound();
        Types = await booking.PublicTypesAsync();
        if (Types.Count == 1) return Redirect($"/book/{Info.Slug}/{Types[0].Slug}");
        return Page();
    }
}
