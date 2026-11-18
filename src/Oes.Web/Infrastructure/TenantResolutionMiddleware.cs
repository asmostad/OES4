using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Oes.Infrastructure.Tenancy;

namespace Oes.Web.Infrastructure;

/// <summary>
/// Sets the request's tenant from the auth cookie's tenant claim, after checking the user
/// is still an active member of that tenant. A stale or forged claim signs the user out.
/// The tenant is never taken from the URL or query string.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, TenantContext tenant, MembershipLookup memberships)
    {
        // API clients get bare status codes (or ProblemDetails), never the HTML error page.
        if (http.Request.Path.StartsWithSegments("/api")
            && http.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } pages)
            pages.Enabled = false;

        // Public pages get their tenant from the link's token or the booking URL, never from a staff sign-in.
        if (http.Request.Path.StartsWithSegments("/portal") || http.Request.Path.StartsWithSegments("/book"))
        {
            // They also act as an anonymous visitor even in a staff member's browser, so nothing
            // done there is attributed to (or hidden from) that staff account.
            http.User = new ClaimsPrincipal(new ClaimsIdentity());
            await next(http);
            return;
        }

        var user = http.User;
        if (user.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirstValue(OesClaims.TenantId), out var tenantId)
            && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            if (!await memberships.IsActiveMemberAsync(userId, tenantId, http.RequestAborted))
            {
                await http.SignOutAsync(IdentityConstants.ApplicationScheme);
                http.User = new ClaimsPrincipal(new ClaimsIdentity());
                if (http.Request.Path.StartsWithSegments("/api"))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                http.Response.Redirect("/Account/Login");
                return;
            }
            tenant.SetTenant(tenantId);
        }

        await next(http);
    }
}

/// <summary>Signed-in users who haven't picked a tenant yet are sent to the tenant picker.</summary>
public sealed class RequireTenantMiddleware(RequestDelegate next)
{
    private static readonly string[] Open = ["/Account", "/api/v1/health", "/health", "/css", "/js", "/img", "/favicon.ico", "/Error", "/portal", "/book"];

    public async Task InvokeAsync(HttpContext http, TenantContext tenant)
    {
        if (http.User.Identity?.IsAuthenticated == true && tenant.TenantId is null
            && !Open.Any(p => http.Request.Path.StartsWithSegments(p)))
        {
            if (http.Request.Path.StartsWithSegments("/api"))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            http.Response.Redirect("/Account/SelectTenant");
            return;
        }
        await next(http);
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // Bootstrap, Bootstrap Icons and Inter load from the same CDNs OES3 uses.
    private const string Csp =
        "default-src 'self'; " +
        "script-src 'self' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com; " +
        "font-src 'self' https://cdn.jsdelivr.net https://fonts.gstatic.com; " +
        "img-src 'self' data: blob:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext http)
    {
        var h = http.Response.Headers;
        h["Content-Security-Policy"] = Csp;
        h["X-Content-Type-Options"] = "nosniff";
        // Token pages must never leak their URL (the token) to other sites.
        h["Referrer-Policy"] = http.Request.Path.StartsWithSegments("/portal") ? "no-referrer" : "strict-origin-when-cross-origin";
        if (http.Request.Path.StartsWithSegments("/portal")) h["X-Robots-Tag"] = "noindex, nofollow";
        h["X-Frame-Options"] = "DENY";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(self)";
        return next(http);
    }
}
