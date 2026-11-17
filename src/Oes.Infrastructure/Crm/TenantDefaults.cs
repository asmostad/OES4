using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

/// <summary>
/// Default pipeline stages and pick lists for a tenant. Idempotent: only adds what is
/// missing, so it can run for new tenants and for existing ones after an upgrade.
/// Caller must be in that tenant's scope (or a platform scope) and save afterwards.
/// </summary>
public static class TenantDefaults
{
    public static readonly (string Name, string Color, string Outcome, int? StaleHours)[] Stages =
    [
        ("New Lead", "#3b82f6", Outcomes.Open, 24),
        ("Contacted", "#06b6d4", Outcomes.Open, 72),
        ("Inspection Scheduled", "#8b5cf6", Outcomes.Open, 168),
        ("Inspected", "#a855f7", Outcomes.Open, 168),
        ("Claim Filed", "#f59e0b", Outcomes.Open, 336),
        ("Estimate Sent", "#f97316", Outcomes.Open, 336),
        ("Won", "#10b981", Outcomes.Won, null),
        ("Lost", "#ef4444", Outcomes.Lost, null),
    ];

    public static readonly Dictionary<string, (string Key, string Label)[]> Lookups = new()
    {
        [LookupTypes.LeadSource] =
        [
            ("door_knock", "Door Knock"), ("referral", "Referral"), ("website", "Website"), ("online_booking", "Online Booking"),
            ("storm_canvass", "Storm Canvass"), ("repeat_customer", "Repeat Customer"), ("phone_in", "Phone In"), ("other", "Other"),
        ],
        [LookupTypes.PropertyContactRole] =
        [
            ("owner", "Owner"), ("co_owner", "Co-owner"), ("tenant", "Tenant"), ("property_manager", "Property Manager"),
            ("agent", "Real Estate Agent"), ("public_adjuster", "Public Adjuster"), ("other", "Other"),
        ],
        [LookupTypes.Peril] =
        [
            ("hail", "Hail"), ("wind", "Wind"), ("tornado", "Tornado"), ("fire", "Fire"), ("water", "Water"),
            ("tree", "Fallen Tree"), ("other", "Other"),
        ],
        [LookupTypes.LostReason] =
        [
            ("price", "Price"), ("competitor", "Chose a competitor"), ("no_damage", "No damage found"),
            ("claim_denied", "Claim denied"), ("no_response", "No response"), ("not_interested", "Not interested"), ("other", "Other"),
        ],
        [LookupTypes.DocumentCategory] =
        [
            ("photos", "Photos"), ("contract", "Contract"), ("insurance", "Insurance"), ("measurement_report", "Measurement Report"),
            ("invoice", "Invoice"), ("permit", "Permit"), ("other", "Other"),
        ],
    };

    public static async Task EnsureAsync(OesDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        await Measurements.MeasurementDefaults.EnsureAsync(db, tenantId, ct);
        var filter = new[] { OesDbContext.TenantFilter };

        if (!await db.PipelineStages.IgnoreQueryFilters(filter).AnyAsync(s => s.TenantId == tenantId, ct))
        {
            var sort = 0;
            foreach (var (name, color, outcome, stale) in Stages)
                db.PipelineStages.Add(new PipelineStage
                {
                    TenantId = tenantId, Name = name, Color = color, Outcome = outcome, StaleAfterHours = stale,
                    Sort = sort += 10, IsDefault = sort == 10,
                });
        }

        var existing = await db.LookupValues.IgnoreQueryFilters(filter).Where(l => l.TenantId == tenantId)
            .Select(l => new { l.Type, l.Key }).ToListAsync(ct);
        foreach (var (type, values) in Lookups)
        {
            var sort = 0;
            foreach (var (key, label) in values)
            {
                sort += 10;
                if (existing.Any(e => e.Type == type && e.Key == key)) continue;
                db.LookupValues.Add(new LookupValue { TenantId = tenantId, Type = type, Key = key, Label = label, Sort = sort, IsSystem = true });
            }
        }
    }
}
