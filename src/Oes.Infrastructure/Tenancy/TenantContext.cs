using Oes.Application.Abstractions;

namespace Oes.Infrastructure.Tenancy;

/// <summary>
/// Scoped holder for the current tenant. The web app sets it from the auth cookie in
/// TenantResolutionMiddleware; jobs set it from their arguments.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private int _platformDepth;

    public Guid? TenantId { get; private set; }
    public bool IsPlatformScope => _platformDepth > 0;

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is empty.", nameof(tenantId));
        if (TenantId is { } existing && existing != tenantId)
            throw new InvalidOperationException("The tenant for this scope is already set and cannot be changed.");
        TenantId = tenantId;
    }

    /// <summary>
    /// Opens a platform scope that allows writes across tenants. Use only in platform code
    /// (tenant provisioning, super-admin tools); callers must audit why.
    /// </summary>
    public IDisposable BeginPlatformScope()
    {
        _platformDepth++;
        return new Scope(this);
    }

    private sealed class Scope(TenantContext owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._platformDepth--;
        }
    }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
