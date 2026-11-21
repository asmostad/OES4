namespace Oes.Domain.Common;

/// <summary>
/// One phone request sent with an Idempotency-Key: the first one does the work and its reply is kept, so a resend
/// (the app retrying after a dropped connection) gets the same reply instead of doing it twice. Kept 7 days.
/// </summary>
public class IdempotencyRecord : TenantEntity
{
    public Guid UserId { get; set; }
    public string Key { get; set; } = "";
    /// <summary>"POST /api/v1/records/opportunity/{id}/notes": a key can't be reused for a different request.</summary>
    public string Request { get; set; } = "";
    /// <summary>Null while the first request is still running.</summary>
    public int? StatusCode { get; set; }
    public string? ResponseJson { get; set; }
}
