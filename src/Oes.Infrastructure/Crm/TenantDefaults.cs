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

    /// <summary>Starter call outcomes. "Stage" is matched by name to the default stages above.</summary>
    public static readonly (string Name, string Color, int? RecycleDays, bool Attempt, string? Stage, string? LostReason, bool DoNotContact)[] Dispositions =
    [
        ("No answer", "#94a3b8", 1, true, null, null, false),
        ("Left voicemail", "#64748b", 2, true, null, null, false),
        ("Call back later", "#0ea5e9", 7, true, "Contacted", null, false),
        ("Spoke - not ready yet", "#f59e0b", 30, true, "Contacted", null, false),
        ("Inspection set", "#10b981", null, true, "Inspection Scheduled", null, false),
        ("Not interested", "#ef4444", null, true, "Lost", "not_interested", false),
        ("Wrong number", "#a855f7", 90, false, null, null, false),
        ("Do not contact", "#334155", null, true, null, null, true),
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
        [Domain.Work.AppointmentTypes.LookupType] =
        [
            ("inspection", "Inspection"), ("adjuster_meeting", "Adjuster meeting"), ("estimate", "Estimate presentation"),
            ("contract_signing", "Contract signing"), ("install", "Install / production"), ("walkthrough", "Final walkthrough"), ("other", "Other"),
        ],
        [Domain.Production.RemovalReasons.Type] =
        [
            ("on_hold", "On hold"), ("waiting_insurance", "Waiting on insurance"), ("customer_cancelled", "Customer cancelled"),
            ("weather", "Weather delay"), ("financing", "Waiting on financing"), ("other", "Other"),
        ],
        [LookupTypes.DocumentCategory] =
        [
            ("photos", "Photos"), ("contract", "Contract"), ("insurance", "Insurance"), ("measurement_report", "Measurement Report"),
            ("invoice", "Invoice"), ("permit", "Permit"), ("other", "Other"),
        ],
    };

    /// <summary>OES3's project and trade stages, now with roles.</summary>
    public static readonly (string Level, string Name, string Role, string Color, int? StaleDays)[] WorkflowStages =
    [
        (Domain.Production.WorkflowLevels.Project, "Verification", Domain.Production.StageRoles.Initial, "#64748b", 3),
        (Domain.Production.WorkflowLevels.Project, "In Production", Domain.Production.StageRoles.ProductionStart, "#2563eb", 21),
        (Domain.Production.WorkflowLevels.Project, "Waiting for Invoice", Domain.Production.StageRoles.Complete, "#d97706", 5),
        (Domain.Production.WorkflowLevels.Project, "Waiting for Payment", Domain.Production.StageRoles.Invoiced, "#f97316", 30),
        (Domain.Production.WorkflowLevels.Project, "Paid", Domain.Production.StageRoles.Paid, "#10b981", null),
        (Domain.Production.WorkflowLevels.Project, "Closed", Domain.Production.StageRoles.Closed, "#334155", null),
        (Domain.Production.WorkflowLevels.Trade, "Trade Setup", Domain.Production.StageRoles.Initial, "#64748b", 5),
        (Domain.Production.WorkflowLevels.Trade, "Material Orders", Domain.Production.StageRoles.MaterialsOrdered, "#8b5cf6", 7),
        (Domain.Production.WorkflowLevels.Trade, "Material Deliveries", Domain.Production.StageRoles.MaterialsDelivered, "#a855f7", 7),
        (Domain.Production.WorkflowLevels.Trade, "Work In Progress", Domain.Production.StageRoles.Labor, "#0d9488", 5),
        (Domain.Production.WorkflowLevels.Trade, "Final Walkthrough", Domain.Production.StageRoles.FinalWalkthrough, "#06b6d4", 5),
        (Domain.Production.WorkflowLevels.Trade, "Completed", Domain.Production.StageRoles.Complete, "#10b981", null),
    ];

    public static async Task EnsureAsync(OesDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        await Measurements.MeasurementDefaults.EnsureAsync(db, tenantId, ct);
        var filter = new[] { OesDbContext.TenantFilter };

        foreach (var level in new[] { Domain.Production.WorkflowLevels.Project, Domain.Production.WorkflowLevels.Trade })
        {
            if (await db.WorkflowStages.IgnoreQueryFilters(filter).AnyAsync(s => s.TenantId == tenantId && s.Level == level, ct)) continue;
            var sort = 0;
            foreach (var w in WorkflowStages.Where(x => x.Level == level))
                db.WorkflowStages.Add(new Domain.Production.WorkflowStage
                {
                    TenantId = tenantId, Level = level, Name = w.Name, Role = w.Role, Color = w.Color, StaleAfterDays = w.StaleDays, Sort = sort += 10,
                });
        }

        if (!await db.PipelineStages.IgnoreQueryFilters(filter).AnyAsync(s => s.TenantId == tenantId, ct))
        {
            var sort = 0;
            foreach (var (name, color, outcome, stale) in Stages)
                db.PipelineStages.Add(new PipelineStage
                {
                    TenantId = tenantId, Name = name, Color = color, Outcome = outcome, StaleAfterHours = stale,
                    Sort = sort += 10, IsDefault = sort == 10, InProspecting = sort <= 20,
                });
        }

        if (!await db.Dispositions.IgnoreQueryFilters(filter).AnyAsync(d => d.TenantId == tenantId, ct))
        {
            var stages = db.PipelineStages.Local.Where(s => s.TenantId == tenantId).ToList();
            if (stages.Count == 0) stages = await db.PipelineStages.IgnoreQueryFilters(filter).Where(s => s.TenantId == tenantId).ToListAsync(ct);
            var sort = 0;
            foreach (var d in Dispositions)
                db.Dispositions.Add(new Disposition
                {
                    TenantId = tenantId, Name = d.Name, Color = d.Color, RecycleDays = d.RecycleDays, CountsAsAttempt = d.Attempt,
                    IsDoNotContact = d.DoNotContact, LostReasonKey = d.LostReason, Sort = sort += 10,
                    MovesToStageId = d.Stage is null ? null : stages.FirstOrDefault(s => s.Name == d.Stage)?.Id,
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

        // Starter templates, only for a company that has none yet (they're the company's to edit or delete).
        if (!await db.MessageTemplates.IgnoreQueryFilters(filter).AnyAsync(t => t.TenantId == tenantId, ct))
            foreach (var (channel, name, subject, body) in Templates)
                db.MessageTemplates.Add(new Domain.Communication.MessageTemplate
                {
                    TenantId = tenantId, Channel = channel, Name = name, Subject = subject, Body = body,
                    Category = Domain.Communication.MessagePurposes.Transactional,
                });
    }

    public static readonly (string Channel, string Name, string? Subject, string Body)[] Templates =
    [
        (Domain.Communication.Channels.Sms, "Inspection reminder", null,
            "Hi {{contact.first_name}}, this is {{user.first_name}} with {{company.name}}. Just confirming your roof inspection at {{property.street}}. Reply here with any questions."),
        (Domain.Communication.Channels.Sms, "On my way", null,
            "Hi {{contact.first_name}}, {{user.first_name}} from {{company.name}} here. I'm on my way to {{property.street}}."),
        (Domain.Communication.Channels.Email, "Thanks for meeting", "Thanks for meeting with {{company.name}}",
            "Hi {{contact.first_name}},\n\nThanks for taking the time to meet with me about {{property.address}}. I'll follow up shortly with next steps.\n\n{{user.name}}\n{{company.name}}\n{{user.phone}}"),
        (Domain.Communication.Channels.Email, "Estimate follow-up", "Your estimate from {{company.name}}",
            "Hi {{contact.first_name}},\n\nI wanted to follow up on the estimate for {{property.address}}. Let me know if you have any questions or would like to go over it together.\n\n{{user.name}}\n{{company.name}}\n{{user.phone}}"),
    ];
}
