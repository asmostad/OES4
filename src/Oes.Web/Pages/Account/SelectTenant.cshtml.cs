using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Web.Pages.Account;

/// <summary>
/// Tenant picker after sign-in (and "Switch company"). Re-issues the auth cookie with the
/// chosen tenant claim after checking membership. Users with one tenant skip the list.
/// </summary>
public class SelectTenantModel(
    SignInManager<AppUser> signIn, UserManager<AppUser> users, MembershipLookup memberships,
    OesDbContext db, TenantContext tenantContext, IClock clock) : PageModel
{
    public List<TenantChoice> Tenants { get; private set; } = [];
    public Guid? LastTenantId { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return await SignOutAndRestart();

        Tenants = await memberships.ActiveTenantsForUserAsync(user.Id, HttpContext.RequestAborted);
        LastTenantId = user.LastTenantId;

        if (Tenants.Count == 0)
        {
            await signIn.SignOutAsync();
            TempData["LoginError"] = "Your account isn't active in any company. Ask your administrator for access.";
            return RedirectToPage("/Account/AccessDenied");
        }

        // One company: nothing to choose. This only re-issues the user's own session cookie.
        return Tenants.Count == 1 ? await EnterAsync(user, Tenants[0].TenantId) : Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid tenantId)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return await SignOutAndRestart();
        return await EnterAsync(user, tenantId);
    }

    private async Task<IActionResult> EnterAsync(AppUser user, Guid tenantId)
    {
        if (!await memberships.IsActiveMemberAsync(user.Id, tenantId, HttpContext.RequestAborted))
            return Forbid();

        var current = User.FindFirstValue(OesClaims.TenantId);
        if (current is null || current != tenantId.ToString())
        {
            // The request may already carry another tenant (switching); write as the chosen one.
            using var _ = current is null ? null : tenantContext.BeginPlatformScope();
            if (current is null) tenantContext.SetTenant(tenantId);

            var membership = await db.Memberships.IgnoreQueryFilters([OesDbContext.TenantFilter])
                .FirstAsync(m => m.UserId == user.Id && m.TenantId == tenantId);
            membership.LastSignInAt = clock.UtcNow;
            user.LastTenantId = tenantId;
            db.AuditEntries.Add(new Domain.Common.AuditEntry
            {
                TenantId = tenantId, UserId = user.Id, Action = "user.entered_tenant",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });
            await db.SaveChangesAsync();

            var auth = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            await signIn.SignInWithClaimsAsync(user, auth.Properties?.IsPersistent ?? false,
                [new Claim(OesClaims.TenantId, tenantId.ToString())]);
        }

        return LocalRedirect(!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }

    private async Task<IActionResult> SignOutAndRestart()
    {
        await signIn.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
