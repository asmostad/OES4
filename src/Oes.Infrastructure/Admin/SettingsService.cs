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

public sealed class EstimateDefaults
{
    public string? Terms { get; set; }
    public string? InsuranceTerms { get; set; }
    public int ValidDays { get; set; } = 30;
    public decimal TaxRate { get; set; }
    public decimal InsuranceOverheadPct { get; set; } = 10m;
    public decimal InsuranceProfitPct { get; set; } = 10m;
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

    public async Task<EstimateDefaults> EstimateDefaultsAsync(CancellationToken ct = default)
    {
        var t = await db.Tenants.FirstAsync(x => x.Id == tenant.TenantId, ct);
        return new EstimateDefaults
        {
            Terms = t.EstimateTerms, InsuranceTerms = t.InsuranceEstimateTerms, ValidDays = t.EstimateValidDays, TaxRate = t.DefaultTaxRate,
            InsuranceOverheadPct = t.InsuranceOverheadPct, InsuranceProfitPct = t.InsuranceProfitPct,
        };
    }

    /// <summary>Defaults copied onto each new estimate (existing estimates keep theirs).</summary>
    public async Task SaveEstimateDefaultsAsync(EstimateDefaults input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SettingsManage);
        if (input.ValidDays is < 1 or > 365) throw new CrmRuleException("Estimates should be valid for 1 to 365 days.");
        if (input.TaxRate is < 0 or > 30) throw new CrmRuleException("Sales tax must be between 0 and 30%.");
        if (input.InsuranceOverheadPct is < 0 or > 100 || input.InsuranceProfitPct is < 0 or > 100) throw new CrmRuleException("Overhead and profit must be between 0 and 100%.");
        static string? Text(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var terms = Text(input.Terms);
        var insurance = Text(input.InsuranceTerms);
        if (terms is { Length: > 20000 } || insurance is { Length: > 20000 }) throw new CrmRuleException("Keep the terms under 20,000 characters.");
        var t = await db.Tenants.FirstAsync(x => x.Id == tenant.TenantId, ct);
        t.EstimateTerms = terms;
        t.InsuranceEstimateTerms = insurance;
        t.EstimateValidDays = input.ValidDays;
        t.DefaultTaxRate = input.TaxRate;
        t.InsuranceOverheadPct = input.InsuranceOverheadPct;
        t.InsuranceProfitPct = input.InsuranceProfitPct;
        audit.Record("settings.estimate_defaults", summary: "Estimate defaults updated");
        await db.SaveChangesAsync(ct);
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
