using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Files;

namespace Oes.Web.Infrastructure;

/// <summary>
/// The app uses bearer tokens; the web uses the cookie. One forwarding scheme picks per request:
/// a bearer token is only honoured on /api, so a token can never sign anyone in to the web pages.
/// </summary>
public static class ApiAuth
{
    public const string Scheme = "oes";

    public static bool IsBearer(HttpContext http) =>
        http.Request.Path.StartsWithSegments("/api")
        && http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
}

public static class ApiPageRoutes
{
    /// <summary>
    /// Also serves a page at an /api address, so the field app's bearer token works on it (for its PDF handlers).
    /// Match-only: links and redirects to the page keep using its own URL. Same authorization as the page.
    /// </summary>
    public static Microsoft.AspNetCore.Mvc.ApplicationModels.PageConventionCollection AddApiPageRoute(
        this Microsoft.AspNetCore.Mvc.ApplicationModels.PageConventionCollection conventions, string page, string template)
    {
        conventions.AddPageRouteModelConvention(page, model => model.Selectors.Add(new Microsoft.AspNetCore.Mvc.ApplicationModels.SelectorModel
        {
            AttributeRouteModel = new Microsoft.AspNetCore.Mvc.ApplicationModels.AttributeRouteModel { Template = template, SuppressLinkGeneration = true },
        }));
        return conventions;
    }
}

/// <summary>
/// Anti-forgery for cookie-authenticated API calls only. A cross-site page can't attach an
/// Authorization header (no CORS here), so bearer calls need no CSRF token. Respects [IgnoreAntiforgeryToken].
/// </summary>
public sealed class CookieOnlyAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var http = ctx.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            || HttpMethods.IsOptions(http.Request.Method) || HttpMethods.IsTrace(http.Request.Method)) return;
        if (ctx.Filters.Any(f => f is IgnoreAntiforgeryTokenAttribute)) return;
        if (ApiAuth.IsBearer(http)) return;
        try { await antiforgery.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { ctx.Result = new AntiforgeryValidationFailedResult(); }
    }
}

/// <summary>Turns service exceptions into ProblemDetails for every API controller.</summary>
public sealed class ApiExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext ctx)
    {
        (int Status, string? Detail)? map = ctx.Exception switch
        {
            RecordNotFoundException => (StatusCodes.Status404NotFound, null),
            RecordForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),
            CrmRuleException e => (StatusCodes.Status422UnprocessableEntity, e.Message),
            FileRejectedException e => (StatusCodes.Status422UnprocessableEntity, e.Message),
            _ => null,
        };
        if (map is not { } m) return;
        ctx.Result = new ObjectResult(new ProblemDetails { Status = m.Status, Detail = m.Detail, Title = m.Status == 404 ? "Not found" : "Request refused" })
        { StatusCode = m.Status };
        ctx.ExceptionHandled = true;
    }
}
