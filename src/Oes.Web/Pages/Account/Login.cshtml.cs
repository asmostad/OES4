using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Oes.Infrastructure.Identity;

namespace Oes.Web.Pages.Account;

[EnableRateLimiting("login")]
public class LoginModel(SignInManager<AppUser> signIn, UserManager<AppUser> users, ILogger<LoginModel> log) : PageModel
{
    [BindProperty, Required, EmailAddress, Display(Name = "Email")]
    public string Email { get; set; } = "";

    [BindProperty, Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is null)
        {
            Error = "Invalid email or password.";
            return Page();
        }

        // Signs in without a tenant first; SelectTenant then re-issues the cookie with the tenant claim.
        var result = await signIn.PasswordSignInAsync(user, Password, RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            log.LogWarning("Locked-out sign-in attempt for user {UserId}", user.Id);
            Error = "This account is temporarily locked after too many attempts. Try again in 15 minutes.";
            return Page();
        }
        if (!result.Succeeded)
        {
            Error = "Invalid email or password.";
            return Page();
        }

        return RedirectToPage("/Account/SelectTenant", new { ReturnUrl = SafeReturnUrl() });
    }

    private string? SafeReturnUrl() => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null;
}
