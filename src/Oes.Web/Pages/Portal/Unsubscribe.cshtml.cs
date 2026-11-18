using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Oes.Infrastructure.Communication;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Portal;

/// <summary>
/// Unsubscribe from marketing email. GET shows a confirm button (so link scanners that open
/// every URL don't unsubscribe people); POST unsubscribes. Mail apps' one-click unsubscribe
/// (RFC 8058) posts here without a form token, so this page skips anti-forgery: the link's
/// token is the credential and the only thing it can do is opt the person out.
/// </summary>
[EnableRateLimiting("portal")]
[IgnoreAntiforgeryToken]
public class UnsubscribeModel(BlastService blasts) : CrmPageModel
{
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    public UnsubscribeView? View { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        View = await blasts.UnsubscribeViewAsync(Token);
        if (View is null) Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await blasts.UnsubscribeAsync(Token)) return await OnGetAsync();
        // One-click clients only need a 2xx; people get the confirmation page.
        if (Request.HasFormContentType && Request.Form["List-Unsubscribe"] == "One-Click") return new OkResult();
        return RedirectToPage(new { token = Token });
    }
}
