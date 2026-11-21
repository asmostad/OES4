using Microsoft.AspNetCore.Mvc;
using Oes.Application.Security;
using Oes.Infrastructure.Scheduling;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Bookings;

/// <summary>Online bookings waiting for someone to create or link the contact.</summary>
[HasPermission(Permissions.BookingManage)]
public class IndexModel(BookingService booking) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public bool All { get; set; }
    public List<InboxRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => Rows = await booking.InboxAsync(onlyNew: !All);

    public Task<IActionResult> OnPostAcceptAsync(Guid id, Guid? contactId) => Guard(async () =>
    {
        var target = await booking.AcceptAsync(id, contactId);
        Flash(contactId is null ? "Added to the CRM." : "Linked to the existing contact.");
        return RedirectToPage(new { all = All });
    });

    public Task<IActionResult> OnPostDismissAsync(Guid id) => Guard(async () =>
    {
        await booking.DismissAsync(id);
        Flash("Booking dismissed and the appointment cancelled.");
        return RedirectToPage(new { all = All });
    });
}
