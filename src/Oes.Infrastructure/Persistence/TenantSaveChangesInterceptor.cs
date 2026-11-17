using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Oes.Application.Abstractions;
using Oes.Domain.Common;
using Oes.Infrastructure.Identity;

namespace Oes.Infrastructure.Persistence;

public sealed class CrossTenantWriteException(string message) : InvalidOperationException(message);

/// <summary>
/// Write side of tenant isolation. On every SaveChanges:
/// <list type="bullet">
/// <item>New tenant-owned rows are stamped with the current tenant. A row already stamped
/// with a different tenant is refused.</item>
/// <item>Updates and deletes of another tenant's row are refused, and TenantId can never change.</item>
/// <item>CreatedAt/UpdatedAt are maintained.</item>
/// </list>
/// Cross-tenant writes are allowed only inside TenantContext.BeginPlatformScope().
/// </summary>
public sealed class TenantSaveChangesInterceptor(ITenantContext tenant, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        var now = clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;

            if (entry.Entity is ITenantOwned owned) Guard(entry, owned);

            switch (entry.Entity)
            {
                case Entity e when entry.State == EntityState.Added:
                    if (e.CreatedAt == default) e.CreatedAt = now;
                    e.UpdatedAt = now;
                    break;
                case Entity e when entry.State == EntityState.Modified:
                    entry.Property(nameof(Entity.CreatedAt)).IsModified = false;
                    e.UpdatedAt = now;
                    break;
                case AppUser u when entry.State == EntityState.Added && u.CreatedAt == default:
                    u.CreatedAt = now;
                    break;
            }
        }
    }

    private void Guard(EntityEntry entry, ITenantOwned owned)
    {
        var current = tenant.TenantId;
        var platform = tenant.IsPlatformScope;
        var type = entry.Metadata.ClrType.Name;

        if (entry.State == EntityState.Added)
        {
            if (owned.TenantId == Guid.Empty)
            {
                owned.TenantId = current ?? throw new CrossTenantWriteException(
                    $"Cannot add {type}: no tenant is set for this request.");
            }
            else if (owned.TenantId != current && !platform)
            {
                throw new CrossTenantWriteException($"Cannot add {type} for another tenant.");
            }
            return;
        }

        var prop = entry.Property(nameof(ITenantOwned.TenantId));
        var original = (Guid)prop.OriginalValue!;
        var now = (Guid)prop.CurrentValue!;

        if (original != now)
            throw new CrossTenantWriteException($"The tenant of a {type} can never change.");

        if (original != current && !platform)
            throw new CrossTenantWriteException(
                $"Cannot {(entry.State == EntityState.Deleted ? "delete" : "update")} {type}: it belongs to another tenant.");
    }
}
