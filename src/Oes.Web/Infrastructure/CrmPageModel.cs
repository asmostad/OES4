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
