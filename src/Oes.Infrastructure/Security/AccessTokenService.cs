using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Tenancy;

namespace Oes.Infrastructure.Security;

/// <summary>
/// Hashed, expiring, revocable links for people who don't sign in (subs now; booking and
/// unsubscribe later). The raw token exists only in the link; the database keeps a SHA-256 hash.
/// </summary>
public sealed class AccessTokenService(OesDbContext db, TenantContext tenant, IClock clock, ICurrentUser user)
{
    public static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>Creates a new link for the record, revoking any earlier ones for the same purpose. Returns the raw token.</summary>
    public async Task<string> IssueAsync(string purpose, string entityType, Guid entityId, TimeSpan lifetime, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        foreach (var old in await db.ExternalAccessTokens.Where(t => t.Purpose == purpose && t.EntityType == entityType && t.EntityId == entityId && t.RevokedAt == null).ToListAsync(ct))
            old.RevokedAt = now;
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        db.ExternalAccessTokens.Add(new ExternalAccessToken
        {
            Purpose = purpose, EntityType = entityType, EntityId = entityId, TokenHash = Hash(raw), ExpiresAt = now.Add(lifetime), CreatedByUserId = user.UserId,
        });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    public async Task RevokeAllAsync(string purpose, string entityType, Guid entityId, CancellationToken ct = default)
    {
        foreach (var t in await db.ExternalAccessTokens.Where(t => t.Purpose == purpose && t.EntityType == entityType && t.EntityId == entityId && t.RevokedAt == null).ToListAsync(ct))
            t.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Looks a raw token up across tenants (the link is the only credential), checks purpose,
    /// expiry and revocation, then sets this scope's tenant from the token. Null when invalid.
    /// </summary>
    public async Task<ExternalAccessToken?> ResolveAsync(string? raw, string purpose, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 100) return null;
        var hash = Hash(raw);
        var token = await db.ExternalAccessTokens.IgnoreQueryFilters([OesDbContext.TenantFilter])
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null || token.Purpose != purpose || token.RevokedAt is not null || token.ExpiresAt <= clock.UtcNow) return null;
        tenant.SetTenant(token.TenantId);
        token.LastUsedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return token;
    }
}
