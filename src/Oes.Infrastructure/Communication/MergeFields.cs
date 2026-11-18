using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Communication;

/// <summary>
/// The one merge-field resolver for email, SMS (and later PDFs). Fields look like {{contact.first_name}}.
/// Unknown or empty fields render as nothing. Values are plain text; callers encode for HTML.
/// </summary>
public sealed partial class MergeFields(OesDbContext db, ITenantContext tenant, ICurrentUser user)
{
    public static readonly (string Key, string Label)[] Available =
    [
        ("contact.first_name", "Contact first name"), ("contact.last_name", "Contact last name"), ("contact.name", "Contact full name"),
        ("property.address", "Property address (one line)"), ("property.street", "Property street"), ("property.city", "Property city"),
        ("opportunity.number", "Opportunity number"), ("opportunity.name", "Opportunity name"),
        ("project.number", "Project number"),
        ("company.name", "Company name"), ("company.address", "Company mailing address"),
        ("user.name", "Your name"), ("user.first_name", "Your first name"), ("user.email", "Your email"), ("user.phone", "Your phone"),
    ];

    [GeneratedRegex(@"\{\{\s*([a-z_]+\.[a-z_]+)\s*\}\}")]
    private static partial Regex Token();

    public static string Render(string? text, IReadOnlyDictionary<string, string> values) =>
        string.IsNullOrEmpty(text) ? "" : Token().Replace(text, m => values.GetValueOrDefault(m.Groups[1].Value, ""));

    /// <summary>Unknown {{fields}} in a template, so authors find typos when saving.</summary>
    public static List<string> Unknown(string? text) =>
        string.IsNullOrEmpty(text) ? [] : Token().Matches(text).Select(m => m.Groups[1].Value).Distinct()
            .Where(k => !Available.Any(a => a.Key == k)).ToList();

    /// <summary>Values for a record (walking up to its opportunity, contact and property) and a specific contact.</summary>
    public async Task<Dictionary<string, string>> ForAsync(string? entityType, Guid? entityId, Guid? contactId, CancellationToken ct = default)
    {
        var v = new Dictionary<string, string>();
        void Set(string k, string? s) { if (!string.IsNullOrWhiteSpace(s)) v[k] = s.Trim(); }

        Guid? oppId = null, propertyId = null, projectId = null;
        switch (entityType)
        {
            case EntityTypes.WorkOrder:
                projectId = await db.WorkOrders.Where(w => w.Id == entityId).Select(w => (Guid?)w.ProjectId).FirstOrDefaultAsync(ct);
                break;
            case EntityTypes.Trade:
                projectId = await db.ProjectTrades.Where(t => t.Id == entityId).Select(t => (Guid?)t.ProjectId).FirstOrDefaultAsync(ct);
                break;
            case EntityTypes.Project: projectId = entityId; break;
            case EntityTypes.Estimate:
                oppId = await db.Estimates.Where(e => e.Id == entityId).Select(e => (Guid?)e.OpportunityId).FirstOrDefaultAsync(ct);
                break;
            case EntityTypes.Claim:
                oppId = await db.Claims.Where(c => c.Id == entityId).Select(c => (Guid?)c.OpportunityId).FirstOrDefaultAsync(ct);
                break;
            case EntityTypes.Opportunity: oppId = entityId; break;
            case EntityTypes.Property: propertyId = entityId; break;
            case EntityTypes.Contact: contactId ??= entityId; break;
        }
        if (projectId is { } pid)
        {
            var p = await db.Projects.Where(x => x.Id == pid).Select(x => new { x.Number, x.OpportunityId, x.PropertyId }).FirstOrDefaultAsync(ct);
            if (p is not null) { Set("project.number", p.Number); oppId ??= p.OpportunityId; propertyId ??= p.PropertyId; }
        }
        if (oppId is { } oid)
        {
            var o = await db.Opportunities.Where(x => x.Id == oid).Select(x => new { x.Number, x.Name, x.PrimaryContactId, x.PropertyId }).FirstOrDefaultAsync(ct);
            if (o is not null)
            {
                Set("opportunity.number", o.Number); Set("opportunity.name", o.Name);
                contactId ??= o.PrimaryContactId; propertyId ??= o.PropertyId;
            }
        }
        if (contactId is { } cid)
        {
            var c = await db.Contacts.Where(x => x.Id == cid).Select(x => new { x.FirstName, x.LastName, x.DisplayName, x.Type }).FirstOrDefaultAsync(ct);
            if (c is not null)
            {
                Set("contact.first_name", c.Type == ContactTypes.Organization ? c.DisplayName : c.FirstName);
                Set("contact.last_name", c.LastName); Set("contact.name", c.DisplayName);
            }
        }
        // Messaging a contact directly: use their (primary, current) property.
        if (propertyId is null && contactId is { } pc)
            propertyId = await db.PropertyContacts.Where(x => x.ContactId == pc && x.EndedOn == null)
                .OrderByDescending(x => x.IsPrimary).ThenByDescending(x => x.CreatedAt).Select(x => (Guid?)x.PropertyId).FirstOrDefaultAsync(ct);
        if (propertyId is { } prid)
        {
            var p = await db.Properties.FirstOrDefaultAsync(x => x.Id == prid, ct);
            if (p is not null) { Set("property.address", p.SingleLine); Set("property.street", $"{p.Line1} {p.Line2}"); Set("property.city", p.City); }
        }
        if (tenant.TenantId is { } tid)
        {
            var t = await db.Tenants.Where(x => x.Id == tid).Select(x => new { x.Name, x.PostalAddress }).FirstOrDefaultAsync(ct);
            Set("company.name", t?.Name); Set("company.address", t?.PostalAddress);
        }
        if (user.UserId is { } uid)
        {
            var u = await db.Users.Where(x => x.Id == uid).Select(x => new { x.FirstName, x.LastName, x.Email, x.PhoneNumber }).FirstOrDefaultAsync(ct);
            if (u is not null)
            {
                Set("user.name", $"{u.FirstName} {u.LastName}"); Set("user.first_name", u.FirstName); Set("user.email", u.Email);
                Set("user.phone", u.PhoneNumber is null ? null : ContactValue.FormatPhone(ContactValue.Phone(u.PhoneNumber), u.PhoneNumber));
            }
        }
        return v;
    }
}
