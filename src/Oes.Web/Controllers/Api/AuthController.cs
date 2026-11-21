using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Identity;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Web.Controllers.Api;

public sealed record TokenRequest(string Email, string Password, Guid? TenantId);
public sealed record RefreshRequest(string RefreshToken);
public sealed record TenantOption(Guid Id, string Name);
public sealed record ChooseTenantResponse(string Error, IReadOnlyList<TenantOption> Tenants);

/// <summary>
/// Sign-in for the mobile app. Access tokens last 30 minutes; refresh tokens 30 days. Refreshing
/// re-checks the security stamp (changed password or "sign out everywhere") and that the user is
/// still an active member of the company, so access ends within one access-token lifetime.
/// Every request also re-checks membership (TenantResolutionMiddleware).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    UserManager<AppUser> users, SignInManager<AppUser> signIn, MembershipLookup memberships,
    IOptionsMonitor<BearerTokenOptions> bearer, OesDbContext db, IClock clock, TenantContext tenant) : ControllerBase
{
    private ProblemDetails Refused(string detail) => new() { Status = 401, Title = "Sign-in refused", Detail = detail };

    [HttpPost("token")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Token(TokenRequest req)
    {
        var user = string.IsNullOrWhiteSpace(req.Email) ? null : await users.FindByEmailAsync(req.Email.Trim());
        if (user is null) return Unauthorized(Refused("Email or password is wrong."));
        var check = await signIn.CheckPasswordSignInAsync(user, req.Password ?? "", lockoutOnFailure: true);
        if (check.IsLockedOut) return StatusCode(StatusCodes.Status429TooManyRequests, Refused("Too many attempts. Try again in 15 minutes."));
        if (!check.Succeeded) return Unauthorized(Refused("Email or password is wrong."));

        var tenants = await memberships.ActiveTenantsForUserAsync(user.Id);
        if (tenants.Count == 0) return StatusCode(StatusCodes.Status403Forbidden, Refused("Your account isn't active in any company."));
        var chosen = req.TenantId is { } t ? tenants.FirstOrDefault(x => x.TenantId == t) : tenants.Count == 1 ? tenants[0] : null;
        if (chosen is null)
        {
            if (req.TenantId is not null) return StatusCode(StatusCodes.Status403Forbidden, Refused("You're not a member of that company."));
            return Conflict(new ChooseTenantResponse("choose_tenant", tenants.Select(x => new TenantOption(x.TenantId, x.Name)).ToList()));
        }

        tenant.SetTenant(chosen.TenantId);
        db.AuditEntries.Add(new Oes.Domain.Common.AuditEntry
        {
            TenantId = chosen.TenantId, UserId = user.Id, Action = "user.app_sign_in", IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();
        return await IssueAsync(user, chosen.TenantId);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        var options = bearer.Get(Microsoft.AspNetCore.Identity.IdentityConstants.BearerScheme);
        var ticket = string.IsNullOrEmpty(req.RefreshToken) ? null : options.RefreshTokenProtector.Unprotect(req.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || expires < clock.UtcNow) return Unauthorized(Refused("Please sign in again."));
        // Null when the password changed or the user signed out everywhere since the token was issued.
        var user = await signIn.ValidateSecurityStampAsync(ticket.Principal);
        if (user is null) return Unauthorized(Refused("Please sign in again."));
        if (!Guid.TryParse(ticket.Principal.FindFirstValue(OesClaims.TenantId), out var tenantId)
            || !await memberships.IsActiveMemberAsync(user.Id, tenantId)) return Unauthorized(Refused("Please sign in again."));
        return await IssueAsync(user, tenantId);
    }

    /// <summary>Ends every session for this user: app refresh tokens stop working and web cookies are rejected within minutes.</summary>
    [HttpPost("signout-everywhere")]
    public async Task<IActionResult> SignOutEverywhere()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        await users.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    private async Task<IActionResult> IssueAsync(AppUser user, Guid tenantId)
    {
        var principal = await signIn.CreateUserPrincipalAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(OesClaims.TenantId, tenantId.ToString()));
        // The bearer handler writes { tokenType, accessToken, expiresIn, refreshToken }.
        return SignIn(principal, Microsoft.AspNetCore.Identity.IdentityConstants.BearerScheme);
    }
}
