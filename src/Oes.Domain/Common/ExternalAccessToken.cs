namespace Oes.Domain.Common;

public static class TokenPurposes
{
    public const string SubPortal = "sub_portal";
    public const string Unsubscribe = "unsubscribe";
    public const string BookingManage = "booking_manage";
}

/// <summary>
/// One pattern for every public link (sub portal now; booking and unsubscribe later): the raw
/// token is shown once and only its SHA-256 hash is stored. Links expire and can be revoked.
/// </summary>
public class ExternalAccessToken : TenantEntity
{
    public string Purpose { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
