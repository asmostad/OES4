namespace Oes.Domain.Common;

/// <summary>
/// Base for every persisted record. IDs are GUID v7 (time-ordered) so the mobile app can
/// create records offline, IDs can't be guessed, and inserts stay index-friendly.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Marks a record that belongs to one tenant. Every type that implements this gets the
/// tenant query filter and the write guard automatically (see OesDbContext and
/// TenantSaveChangesInterceptor). Never set TenantId by hand in application code.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}

public abstract class TenantEntity : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
}
