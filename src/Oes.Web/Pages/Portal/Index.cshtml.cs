using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Subcontractors;
using Oes.Web.Infrastructure;

namespace Oes.Web.Pages.Portal;

/// <summary>
/// The sub portal. No account: the link's token is the only credential and is re-verified on
/// every request (expired, revoked or cancelled links show "not available"). Rate limited.
/// </summary>
[EnableRateLimiting("portal")]
[RequestSizeLimit(100 * 1024 * 1024)]
public class IndexModel(PortalService portal) : CrmPageModel
{
    private static readonly FileExtensionContentTypeProvider Types = new();

    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    public PortalView? View { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        View = await portal.GetAsync(Token);
        if (View is null) Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }

    public async Task<IActionResult> OnPostRespondAsync(bool accept, string name, string? reason)
    {
        try
        {
            if (!await portal.RespondAsync(Token, accept, name, reason)) return await OnGetAsync();
            Flash(accept ? "Thanks, you've accepted this work order." : "Thanks for letting us know.");
            return RedirectToPage(new { token = Token });
        }
        catch (CrmRuleException ex)
        {
            Error = ex.Message;
            return await OnGetAsync();
        }
    }

    public async Task<IActionResult> OnPostCompleteAsync(string? note, List<IFormFile>? photos)
    {
        var streams = new List<Stream>();
        try
        {
            var uploads = new List<(Stream, string, string)>();
            foreach (var f in (photos ?? []).Where(f => f.Length > 0))
            {
                var s = f.OpenReadStream();
                streams.Add(s);
                uploads.Add((s, f.FileName, Types.TryGetContentType(f.FileName, out var t) ? t : "application/octet-stream"));
            }
            if (!await portal.CompleteAsync(Token, note, uploads)) return await OnGetAsync();
            Flash("Marked complete. Thank you!");
            return RedirectToPage(new { token = Token });
        }
        catch (CrmRuleException ex)
        {
            Error = ex.Message;
            return await OnGetAsync();
        }
        finally
        {
            foreach (var s in streams) await s.DisposeAsync();
        }
    }
}
