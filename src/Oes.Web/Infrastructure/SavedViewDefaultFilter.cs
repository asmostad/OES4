using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Oes.Infrastructure.Crm;

namespace Oes.Web.Infrastructure;

/// <summary>
/// Opening a list page with no query string sends the user to their default saved view for it.
/// "No filters" in the Views menu adds ?reset=1, so everything is always one click away.
/// </summary>
public sealed class SavedViewDefaultFilter(SavedViewService views) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var req = context.HttpContext.Request;
        if (HttpMethods.IsGet(req.Method) && !req.QueryString.HasValue && context.HttpContext.User.Identity?.IsAuthenticated == true
            && SavedViewService.Lists.Any(l => string.Equals(l.Path, req.Path.Value?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            && await views.DefaultUrlForPathAsync(req.Path.Value!) is { } url)
        {
            context.Result = new LocalRedirectResult(url);
            return;
        }
        await next();
    }
}
