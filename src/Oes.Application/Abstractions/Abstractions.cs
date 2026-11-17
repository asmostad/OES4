namespace Oes.Application.Abstractions;

/// <summary>
/// The tenant the current request or job runs as. Set once per request (from the signed-in
/// user's tenant claim) or per job (from the job's arguments), then read everywhere.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    /// <summary>True only inside an explicit, audited platform scope (super-admin tools, platform jobs).</summary>
    bool IsPlatformScope { get; }

    Guid RequiredTenantId => TenantId ?? throw new InvalidOperationException("No tenant is set for this request.");
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    Task<bool> HasPermissionAsync(string permissionKey, CancellationToken ct = default);
    Task<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken ct = default);
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed record StoredFile(string Key, string FileName, string ContentType, long Length);

/// <summary>
/// File storage. DiskFileStore now; an S3 implementation later with no caller changes.
/// Keys are opaque, tenant-scoped and never derived from user input.
/// </summary>
public interface IFileStore
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}

public sealed record SystemEmail(string To, string Subject, string HtmlBody, string? TextBody = null);

/// <summary>
/// Platform email (invites, password resets, staff notices) from our own domain via SES.
/// Email to CRM contacts goes through the tenant's own mailbox instead (IMailProvider, later).
/// </summary>
public interface ISystemMailer
{
    Task SendAsync(SystemEmail email, CancellationToken ct = default);
}

public interface IAuditLog
{
    /// <summary>Adds an audit row to the current unit of work; it is saved with the caller's SaveChanges.</summary>
    void Record(string action, string? entityType = null, Guid? entityId = null, string? summary = null, object? details = null);
}

public interface INumberSequence
{
    /// <summary>Issues the next display number for this tenant, e.g. P-2026-0311.</summary>
    Task<string> NextAsync((string Key, string Prefix) kind, CancellationToken ct = default);
}
