using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Oes.Application.Abstractions;
using Oes.Domain.Common;

namespace Oes.Infrastructure.Persistence;

public sealed class AuditLog(OesDbContext db, ICurrentUser user, IHttpContextAccessor http) : IAuditLog
{
    public void Record(string action, string? entityType = null, Guid? entityId = null, string? summary = null, object? details = null)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            UserId = user.UserId,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
        });
    }
}

/// <summary>
/// Atomic per-tenant counters. Uses MySQL's LAST_INSERT_ID(expr) trick so concurrent
/// callers never get the same number, without locking the whole table.
/// </summary>
public sealed class NumberSequence(OesDbContext db, ITenantContext tenant, IClock clock) : INumberSequence
{
    public async Task<string> NextAsync((string Key, string Prefix) kind, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant is set.");
        var year = clock.UtcNow.Year;
        var value = await NextValueAsync(tenantId, kind.Key, year, ct);
        return DisplayNumber.Format(kind.Prefix, year, value);
    }

    private async Task<long> NextValueAsync(Guid tenantId, string key, int year, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText =
                "INSERT INTO tenant_sequences (TenantId, `Key`, `Year`, LastValue) VALUES (@t, @k, @y, LAST_INSERT_ID(1)) " +
                "ON DUPLICATE KEY UPDATE LastValue = LAST_INSERT_ID(LastValue + 1); SELECT LAST_INSERT_ID();";
            Add(cmd, "@t", tenantId.ToByteArray(true));
            Add(cmd, "@k", key);
            Add(cmd, "@y", year);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    private static void Add(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
