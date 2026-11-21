using Microsoft.AspNetCore.Identity;

namespace Oes.Infrastructure.Identity;

/// <summary>
/// A person who signs in. Users are platform-level (one login across tenants); what they
/// can do inside a tenant comes from their TenantMembership and its roles.
/// </summary>
public class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? AvatarFileKey { get; set; }

    /// <summary>Platform operator (us). Grants the platform console only, never tenant data by itself.</summary>
    public bool IsSuperAdmin { get; set; }

    /// <summary>Tenant chosen at the last sign-in; preselected next time.</summary>
    public Guid? LastTenantId { get; set; }

    public DateTime CreatedAt { get; set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public string Initials =>
        string.Concat(new[] { FirstName, LastName }.Where(s => s.Length > 0).Select(s => char.ToUpperInvariant(s[0])));
}
