using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Oes.Web.Pages.Dev;

/// <summary>Development-only preview of the record-level headers and breadcrumbs.</summary>
public class LevelsModel(IWebHostEnvironment env) : PageModel
{
    public IActionResult OnGet() => env.IsDevelopment() ? Page() : NotFound();
}
