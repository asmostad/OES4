using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Infrastructure.Persistence;

namespace Oes.Web.Infrastructure;

/// <summary>
/// Makes a create endpoint safe to repeat when the request carries an <c>Idempotency-Key</c> header (the phone app's
/// offline queue sends one per queued action and keeps it through every retry). The first request with a key runs
/// and its successful reply is stored; a repeat gets that reply back (with <c>Idempotent-Replayed: true</c>) without
/// running again. A repeat that arrives while the first is still running gets 409; a key reused for a different
/// request gets 422. Failed requests aren't stored, so they can be retried. Without the header nothing changes.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;
    public IFilterMetadata CreateInstance(IServiceProvider services) => ActivatorUtilities.CreateInstance<IdempotencyFilter>(services);
}

public sealed partial class IdempotencyFilter(OesDbContext db, ICurrentUser user, IClock clock) : IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[A-Za-z0-9_-]{8,100}$")]
    private static partial Regex ValidKey();

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var http = ctx.HttpContext;
        var key = http.Request.Headers[Header].ToString();
        if (key.Length == 0 || user.UserId is not { } me) { await next(); return; }
        if (!ValidKey().IsMatch(key)) { ctx.Result = Problem(400, "Idempotency-Key must be 8 to 100 letters, digits, - or _."); return; }
        var request = $"{http.Request.Method} {http.Request.Path}";
        var ct = http.RequestAborted;

        var existing = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == me && r.Key == key, ct);
        if (existing is not null) { ctx.Result = Replay(existing, request, http); return; }

        // Claim the key before running. The unique index makes a simultaneous duplicate fail here.
        var cutoff = clock.UtcNow - Keep;
        await db.IdempotencyRecords.Where(r => r.UserId == me && r.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        var record = new IdempotencyRecord { UserId = me, Key = key, Request = request };
        db.IdempotencyRecords.Add(record);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            var winner = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == me && r.Key == key, ct);
            ctx.Result = winner is null ? Problem(409, "That request is already being handled. Try again shortly.") : Replay(winner, request, http);
            return;
        }
        db.Entry(record).State = EntityState.Detached; // the action's own SaveChanges must never touch it

        var done = await next();

        // Store a success; forget anything else so it can be retried. Direct statements: no tracked changes are saved here.
        var status = done.Exception is not null && !done.ExceptionHandled ? 500 : StatusOf(done.Result);
        if (status is >= 200 and < 300)
        {
            var body = done.Result is ObjectResult { Value: { } value } ? JsonSerializer.Serialize(value, value.GetType(), Json) : null;
            await db.IdempotencyRecords.Where(r => r.Id == record.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.StatusCode, status).SetProperty(r => r.ResponseJson, body), CancellationToken.None);
        }
        else await db.IdempotencyRecords.Where(r => r.Id == record.Id).ExecuteDeleteAsync(CancellationToken.None);
    }

    private static int StatusOf(IActionResult? result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        IStatusCodeActionResult s => s.StatusCode ?? 200,
        null => 200,
        _ => 200,
    };

    private static IActionResult Replay(IdempotencyRecord r, string request, HttpContext http)
    {
        if (r.Request != request) return Problem(422, "That Idempotency-Key was already used for a different request.");
        if (r.StatusCode is not { } status) return Problem(409, "That request is already being handled. Try again shortly.");
        http.Response.Headers["Idempotent-Replayed"] = "true";
        return r.ResponseJson is null ? new StatusCodeResult(status)
            : new ContentResult { StatusCode = status, Content = r.ResponseJson, ContentType = "application/json; charset=utf-8" };
    }

    private static ObjectResult Problem(int status, string detail) =>
        new(new ProblemDetails { Status = status, Title = "Request refused", Detail = detail }) { StatusCode = status };
}
