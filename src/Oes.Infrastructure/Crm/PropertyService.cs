using Microsoft.EntityFrameworkCore;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

public sealed class PropertyInput
{
    public string Line1 { get; set; } = "";
    public string? Line2 { get; set; }
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Kind { get; set; } = PropertyKinds.Residential;
    public string? Notes { get; set; }
}

public sealed class DuplicatePropertyException(Guid existingId, string address)
    : Exception($"{address} already exists.")
{
    public Guid ExistingId { get; } = existingId;
}

public sealed record PropertyRow(Guid Id, string Line1, string? Line2, string CityLine, string Kind, string? PrimaryContact,
    int OpenOpportunities, DateTime CreatedAt);

public sealed record PropertyContactRow(Guid LinkId, Guid ContactId, string Name, string Type, string RoleKey, bool IsPrimary,
    bool IsCurrent, DateOnly? StartedOn, DateOnly? EndedOn, string? Email, string? Phone);

public sealed record PropertyDetail(Property Property, List<PropertyStructure> Structures, List<PropertyContactRow> Contacts,
    List<OpportunityBrief> Opportunities, bool CanEdit);

public sealed record PropertyMatch(Guid Id, string Address, bool Exact);

public sealed class PropertyService(OesDbContext db, RecordScope scope, RecordItems items, ContactService contacts)
{
    public async Task<PagedResult<PropertyRow>> SearchAsync(string? q, string? kind, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var props = await scope.PropertiesAsync();
        if (!string.IsNullOrWhiteSpace(kind)) props = props.Where(p => p.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var key = AddressKey.NormalizeLine(term);
            props = props.Where(p => p.Line1.Contains(term) || p.City.Contains(term) || p.PostalCode.StartsWith(term)
                                     || (key.Length > 0 && p.NormalizedKey.Contains(key))
                                     || db.PropertyContacts.Any(pc => pc.PropertyId == p.Id && pc.EndedOn == null
                                         && db.Contacts.Any(c => c.Id == pc.ContactId && c.DisplayName.Contains(term))));
        }

        var total = await props.CountAsync(ct);
        page = Math.Max(1, page);
        var rows = await props.OrderBy(p => p.Line1).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                p.Id, p.Line1, p.Line2, p.City, p.State, p.PostalCode, p.Kind, p.CreatedAt,
                Contact = (from pc in db.PropertyContacts
                           join c in db.Contacts on pc.ContactId equals c.Id
                           where pc.PropertyId == p.Id && pc.EndedOn == null
                           orderby pc.IsPrimary descending
                           select c.DisplayName).FirstOrDefault(),
                Open = db.Opportunities.Count(o => o.PropertyId == p.Id && o.Outcome == Outcomes.Open && o.ArchivedAt == null),
            }).ToListAsync(ct);

        return new PagedResult<PropertyRow>(rows.Select(r => new PropertyRow(r.Id, r.Line1, r.Line2,
            new Property { City = r.City, State = r.State, PostalCode = r.PostalCode }.CityLine, r.Kind, r.Contact, r.Open, r.CreatedAt)).ToList(),
            total, page, pageSize);
    }

    /// <summary>
    /// Exact matches by normalized key across the whole tenant (so a rep can't create a
    /// duplicate of a house they can't see), plus near matches (same street number and ZIP)
    /// among visible properties.
    /// </summary>
    public async Task<List<PropertyMatch>> FindMatchesAsync(PropertyInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Line1)) return [];
        var key = AddressKey.For(input.Line1, input.Line2, input.City, input.State, input.PostalCode);
        var visible = await scope.PropertiesAsync();
        var matches = new List<PropertyMatch>();

        var exact = await db.Properties.Where(p => p.NormalizedKey == key).Select(p => new { p.Id }).FirstOrDefaultAsync(ct);
        if (exact is not null)
        {
            var canSee = await visible.AnyAsync(p => p.Id == exact.Id, ct);
            var p = await db.Properties.FirstAsync(x => x.Id == exact.Id, ct);
            matches.Add(new PropertyMatch(exact.Id, canSee ? p.SingleLine : "an existing property (owned by another rep)", true));
        }

        var number = AddressKey.NormalizeLine(input.Line1).Split(' ').FirstOrDefault() ?? "";
        var zip = AddressKey.Zip5(input.PostalCode);
        if (number.Length > 0 && zip.Length == 5)
        {
            var prefix = number + " ";
            var near = await visible.Where(p => p.NormalizedKey.StartsWith(prefix) && p.NormalizedKey.EndsWith("|" + zip) && p.NormalizedKey != key)
                .Take(5).ToListAsync(ct);
            matches.AddRange(near.Select(p => new PropertyMatch(p.Id, p.SingleLine, false)));
        }
        return matches;
    }

    public async Task<Guid> CreateAsync(PropertyInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.RecordsCreate);
        var p = new Property { CreatedByUserId = scope.Me };
        Apply(p, input);

        var existing = await db.Properties.Where(x => x.NormalizedKey == p.NormalizedKey).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (existing != Guid.Empty) throw new DuplicatePropertyException(existing, p.SingleLine);

        p.Structures.Add(new PropertyStructure { PropertyId = p.Id, Name = "Main House", Kind = "house", Sort = 10 });
        db.Properties.Add(p);
        await items.LogAsync(EntityTypes.Property, p.Id, "property.created", "Property created", ct);
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    public async Task UpdateAsync(Guid id, PropertyInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.RecordsEditOwn);
        var p = await (await scope.PropertiesAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Property");
        Apply(p, input);
        var clash = await db.Properties.Where(x => x.NormalizedKey == p.NormalizedKey && x.Id != id).Select(x => x.Id).FirstOrDefaultAsync(ct);
        if (clash != Guid.Empty) throw new DuplicatePropertyException(clash, p.SingleLine);
        await items.LogAsync(EntityTypes.Property, id, "property.updated", "Address or details updated", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PropertyDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await (await scope.PropertiesAsync(includeArchived: true)).Include(x => x.Structures).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new RecordNotFoundException("Property");

        var links = await (from pc in db.PropertyContacts where pc.PropertyId == id
                           join c in db.Contacts on pc.ContactId equals c.Id
                           orderby pc.EndedOn != null, pc.IsPrimary descending, c.DisplayName
                           select new
                           {
                               pc.Id, ContactId = c.Id, c.DisplayName, c.Type, pc.RoleKey, pc.IsPrimary, pc.StartedOn, pc.EndedOn,
                               Email = db.ContactMethods.Where(m => m.ContactId == c.Id && m.Kind == ContactMethodKinds.Email)
                                   .OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
                               Phone = db.ContactMethods.Where(m => m.ContactId == c.Id && (m.Kind == ContactMethodKinds.Mobile || m.Kind == ContactMethodKinds.Phone))
                                   .OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
                           }).ToListAsync(ct);

        var opps = await contacts.OpportunityBriefsAsync((await scope.OpportunitiesAsync()).Where(o => o.PropertyId == id), ct);

        return new PropertyDetail(p, p.Structures.OrderBy(s => s.Sort).ToList(),
            links.Select(l => new PropertyContactRow(l.Id, l.ContactId, l.DisplayName, l.Type, l.RoleKey, l.IsPrimary, l.EndedOn == null,
                l.StartedOn, l.EndedOn, l.Email, l.Phone)).ToList(),
            opps, await scope.CanEditAsync(null));
    }

    public async Task LinkContactAsync(Guid propertyId, Guid contactId, string roleKey, bool isPrimary, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(EntityTypes.Property, propertyId);
        await scope.EnsureCanSeeAsync(EntityTypes.Contact, contactId);
        if (await db.PropertyContacts.AnyAsync(pc => pc.PropertyId == propertyId && pc.ContactId == contactId && pc.EndedOn == null, ct))
            throw new CrmRuleException("That contact is already linked to this property.");
        await AddLinkAsync(propertyId, contactId, roleKey, isPrimary, ct);
        var name = await db.Contacts.Where(c => c.Id == contactId).Select(c => c.DisplayName).FirstAsync(ct);
        await items.LogAsync(EntityTypes.Property, propertyId, "property.contact_linked", $"{name} linked as {roleKey.Replace('_', ' ')}", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds a property ↔ contact link (no save). The first current link becomes primary.</summary>
    internal async Task AddLinkAsync(Guid propertyId, Guid contactId, string roleKey, bool isPrimary, CancellationToken ct)
    {
        var hasCurrent = await db.PropertyContacts.AnyAsync(pc => pc.PropertyId == propertyId && pc.EndedOn == null, ct);
        if (isPrimary && hasCurrent)
            await db.PropertyContacts.Where(pc => pc.PropertyId == propertyId && pc.IsPrimary)
                .ExecuteUpdateAsync(s => s.SetProperty(pc => pc.IsPrimary, false), ct);
        db.PropertyContacts.Add(new PropertyContact
        {
            PropertyId = propertyId, ContactId = contactId, RoleKey = string.IsNullOrWhiteSpace(roleKey) ? "owner" : roleKey,
            IsPrimary = isPrimary || !hasCurrent, StartedOn = DateOnly.FromDateTime(DateTime.UtcNow),
        });
    }

    /// <summary>Ends a link (e.g. the house sold). History is kept.</summary>
    public async Task EndContactLinkAsync(Guid linkId, CancellationToken ct = default)
    {
        var link = await db.PropertyContacts.FirstOrDefaultAsync(pc => pc.Id == linkId, ct) ?? throw new RecordNotFoundException("Link");
        await scope.EnsureCanSeeAsync(EntityTypes.Property, link.PropertyId);
        link.EndedOn = DateOnly.FromDateTime(DateTime.UtcNow);
        link.IsPrimary = false;
        var name = await db.Contacts.Where(c => c.Id == link.ContactId).Select(c => c.DisplayName).FirstAsync(ct);
        await items.LogAsync(EntityTypes.Property, link.PropertyId, "property.contact_ended", $"{name} no longer linked", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddStructureAsync(Guid propertyId, string name, CancellationToken ct = default)
    {
        await scope.EnsureCanSeeAsync(EntityTypes.Property, propertyId);
        name = name.Trim();
        if (name.Length == 0) throw new CrmRuleException("Structure name is required.");
        var max = await db.PropertyStructures.Where(s => s.PropertyId == propertyId).MaxAsync(s => (int?)s.Sort, ct) ?? 0;
        db.PropertyStructures.Add(new PropertyStructure { PropertyId = propertyId, Name = name, Sort = max + 10 });
        await items.LogAsync(EntityTypes.Property, propertyId, "property.structure_added", $"Structure \"{name}\" added", ct);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Property p, PropertyInput input)
    {
        p.Line1 = Required(input.Line1, "Street address");
        p.Line2 = ContactService.Clean(input.Line2);
        p.City = Required(input.City, "City");
        p.State = Required(input.State, "State").ToUpperInvariant();
        p.PostalCode = Required(input.PostalCode, "ZIP code");
        p.Kind = PropertyKinds.All.Contains(input.Kind) ? input.Kind : PropertyKinds.Residential;
        p.Notes = ContactService.Clean(input.Notes);
        p.NormalizedKey = AddressKey.For(p.Line1, p.Line2, p.City, p.State, p.PostalCode);
    }

    private static string Required(string? s, string label) =>
        string.IsNullOrWhiteSpace(s) ? throw new CrmRuleException($"{label} is required.") : s.Trim();
}
