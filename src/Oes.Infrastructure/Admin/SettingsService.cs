using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Admin;

public sealed class CompanySettings
{
    public string Name { get; set; } = "";
    public string TimeZone { get; set; } = "America/Chicago";
    public string? PostalAddress { get; set; }
}

/// <summary>The current company's own settings (name, time zone, mailing address).</summary>
public sealed class SettingsService(OesDbContext db, ITenantContext tenant, RecordScope scope, IAuditLog audit)
{
    public static readonly (string Id, string Label)[] TimeZones =
    [
        ("America/New_York", "Eastern"), ("America/Chicago", "Central"), ("America/Denver", "Mountain"),
        ("America/Phoenix", "Arizona (no DST)"), ("America/Los_Angeles", "Pacific"), ("America/Anchorage", "Alaska"), ("Pacific/Honolulu", "Hawaii"),
    ];

    public async Task<CompanySettings> GetAsync(CancellationToken ct = default)
    {
        var t = await db.Tenants.FirstAsync(x => x.Id == tenant.TenantId, ct);
        return new CompanySettings { Name = t.Name, TimeZone = t.TimeZone, PostalAddress = t.PostalAddress };
    }

    public async Task SaveAsync(CompanySettings input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SettingsManage);
        var name = input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 150) throw new CrmRuleException("Enter the company name.");
        if (!TimeZones.Any(z => z.Id == input.TimeZone)) throw new CrmRuleException("Pick a time zone.");
        var address = string.IsNullOrWhiteSpace(input.PostalAddress) ? null : input.PostalAddress.Trim();
        if (address is { Length: > 300 }) throw new CrmRuleException("Keep the mailing address under 300 characters.");
        var t = await db.Tenants.FirstAsync(x => x.Id == tenant.TenantId, ct);
        t.Name = name; t.TimeZone = input.TimeZone; t.PostalAddress = address;
        audit.Record("settings.updated", summary: "Company settings updated");
        await db.SaveChangesAsync(ct);
    }
}
