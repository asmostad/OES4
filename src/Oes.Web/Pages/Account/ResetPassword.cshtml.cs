using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Oes.Infrastructure.Identity;

namespace Oes.Web.Pages.Account;

[EnableRateLimiting("login")]
public class ResetPasswordModel(UserManager<AppUser> users) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string Code { get; set; } = "";

    [BindProperty, Required, DataType(DataType.Password), StringLength(200, MinimumLength = 12), Display(Name = "New password")]
    public string Password { get; set; } = "";

    [BindProperty, Required, DataType(DataType.Password), Compare(nameof(Password)), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";

    public bool Done { get; private set; }
    public List<string> Errors { get; } = [];

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByIdAsync(UserId.ToString());
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code)); }
        catch (FormatException) { token = ""; }

        var result = user is null
            ? IdentityResult.Failed(new IdentityError { Description = "This reset link is invalid or has expired." })
            : await users.ResetPasswordAsync(user, token, Password);

        if (!result.Succeeded)
        {
            Errors.AddRange(result.Errors.Select(e => e.Code == "InvalidToken" ? "This reset link is invalid or has expired." : e.Description));
            return Page();
        }

        await users.ResetAccessFailedCountAsync(user!);
        Done = true;
        return Page();
    }
}
