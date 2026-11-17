using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Persistence;

namespace Oes.Web.Controllers.Api;

public sealed record MeResponse(Guid UserId, string FirstName, string LastName, string Email, Guid TenantId, string TenantName,
    IReadOnlyList<string> Permissions);

/// <summary>Who am I, which tenant, what can I do. The web JS and the mobile app both start here.</summary>
[ApiController]
[Route("api/v1/me")]
public sealed class MeController(OesDbContext db, ICurrentUser user, ITenantContext tenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken ct)
    {
        var userId = user.UserId!.Value;
        var tenantId = tenant.TenantId!.Value;
        var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
        var tenantName = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).FirstAsync(ct);
        var perms = await user.GetPermissionsAsync(ct);
        return new MeResponse(u.Id, u.FirstName, u.LastName, u.Email ?? "", tenantId, tenantName, perms.Order().ToList());
    }
}
