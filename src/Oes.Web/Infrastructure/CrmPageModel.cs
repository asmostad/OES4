using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oes.Infrastructure.Crm;

namespace Oes.Web.Infrastructure;

/// <summary>
/// Base for CRM pages: turns service exceptions into the right response. Not found and
/// not visible look the same (404) so record IDs can't be probed.
/// </summary>
public abstract class CrmPageModel : PageModel
{
    protected void Flash(string message, string type = "success")
    {
        TempData["Flash"] = message;
        TempData["FlashType"] = type;
    }

    protected async Task<IActionResult> Guard(Func<Task<IActionResult>> action, Func<IActionResult>? onRuleError = null)
    {
        try
        {
            return await action();
        }
        catch (RecordNotFoundException)
        {
            return NotFound();
        }
        catch (RecordForbiddenException ex)
        {
            Flash(ex.Message, "danger");
            return onRuleError?.Invoke() ?? RedirectToPage();
        }
        catch (CrmRuleException ex)
        {
            if (onRuleError is null)
            {
                Flash(ex.Message, "danger");
                return RedirectToPage();
            }
            ModelState.AddModelError("", ex.Message);
            return onRuleError();
        }
    }

    /// <summary>
    /// An optional address form (bound as "Address") left blank isn't an error. Its non-nullable strings would
    /// otherwise count as required; PropertyService checks the address properly when a street is entered.
    /// </summary>
    protected void ClearOptionalAddress(Oes.Infrastructure.Crm.PropertyInput address)
    {
        if (!string.IsNullOrWhiteSpace(address.Line1)) return;
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("Address.", StringComparison.Ordinal)).ToList()) ModelState.Remove(key);
    }

    /// <summary>
    /// This page, rendered as a PDF download: the page's own Razor view in PDF mode (its layout inlines
    /// the styles and drops the toolbar and scripts), then the server's browser prints it. Load the
    /// page's data first. If no browser is available, it flashes why and goes to <paramref name="printUrl"/>.
    /// </summary>
    protected Task<IActionResult> PdfAsync(string fileName, string? footer, string printUrl)
    {
        ViewData[PdfDocument.ViewDataKey] = true;
        return Task.FromResult<IActionResult>(new PdfPageResult(fileName, footer, printUrl));
    }

    protected IActionResult BackTo(string? returnUrl, string fallback) =>
        LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : fallback);
}

public static class Fmt
{
    public static string Cap(string? s) =>
        string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' ');

    public static string Money(decimal? v) => v is null ? "—" : v.Value.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));

    public static string Size(long? bytes) => bytes switch
    {
        null => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };

    public static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours}h ago";
        if (d.TotalDays < 30) return $"{(int)d.TotalDays}d ago";
        return utc.ToString("MMM d, yyyy");
    }

    public static string StatusBadge(string status) => status switch
    {
        "customer" => "badge-customer",
        "past_customer" => "badge-stage",
        _ => "badge-lead",
    };
}
