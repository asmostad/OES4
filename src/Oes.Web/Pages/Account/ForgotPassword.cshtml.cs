using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Identity;

namespace Oes.Web.Pages.Account;

[EnableRateLimiting("login")]
public class ForgotPasswordModel(
    UserManager<AppUser> users, ISystemMailer mailer, IConfiguration config, ILogger<ForgotPasswordModel> log) : PageModel
{
    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = "";

    public bool Sent { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // Same response whether or not the address exists, so it can't be used to probe accounts.
        Sent = true;
        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is null || await users.IsLockedOutAsync(user)) return Page();

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        // Built from configuration, never the request's Host header (which a caller controls).
        var baseUrl = config["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl is not configured.");
        var link = $"{baseUrl.TrimEnd('/')}/Account/ResetPassword?userId={user.Id}&code={code}";

        await mailer.SendAsync(new SystemEmail(
            user.Email!,
            "Reset your OES password",
            $"""
             <p>Hi {HtmlEncoder.Default.Encode(user.FirstName)},</p>
             <p>Someone asked to reset the password for your OES account. If it was you, use this link within 24 hours:</p>
             <p><a href="{HtmlEncoder.Default.Encode(link)}">Reset my password</a></p>
             <p>If you didn't ask for this, you can ignore this email; your password won't change.</p>
             """,
            $"Reset your OES password: {link}"));
        log.LogInformation("Password reset email sent to user {UserId}", user.Id);
        return Page();
    }
}
