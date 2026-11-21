using Microsoft.EntityFrameworkCore;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

public sealed record MethodInput(string Kind, string Value, string? Label, bool IsPrimary);

public sealed class ContactInput
{
    public string Type { get; set; } = ContactTypes.Person;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? OrganizationName { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? SourceKey { get; set; }
    public string? CautionNote { get; set; }
    public string? Notes { get; set; }
    public List<MethodInput> Methods { get; set; } = [];

    /// <summary>On create only: the organization this person works for, and their title there.</summary>
    public Guid? OrganizationId { get; set; }
    public string? Title { get; set; }
}

public sealed record ContactRow(
    Guid Id, string Type, string DisplayName, string Status, string? Email, string? Phone, string? OwnerName,
    int OpenOpportunities, DateTime CreatedAt);

public sealed record ContactPropertyRow(Guid LinkId, Guid PropertyId, string Address, string RoleKey, bool IsPrimary, bool IsCurrent);

public sealed record ContactRelationRow(Guid Id, Guid OtherContactId, string OtherName, string? Title, bool IsPrimary, string? Email, string? Phone);

public sealed record OpportunityBrief(Guid Id, string Number, string Name, string StageName, string StageColor, string Outcome,
    string? Address, DateTime CreatedAt);

public sealed record ContactDetail(
    Contact Contact, string? OwnerName, List<ContactMethod> Methods, List<ContactPropertyRow> Properties,
    List<ContactRelationRow> Organizations, List<ContactRelationRow> People, List<OpportunityBrief> Opportunities, bool CanEdit,
    List<ContactAddress> Addresses);

public sealed class AddressInput
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
}

public sealed record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize)
{
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed class ContactService(OesDbContext db, RecordScope scope, RecordItems items)
{
    public async Task<PagedResult<ContactRow>> SearchAsync(string? q, string? type, string? status, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        var contacts = await scope.ContactsAsync();
        if (!string.IsNullOrWhiteSpace(type)) contacts = contacts.Where(c => c.Type == type);
        if (!string.IsNullOrWhiteSpace(status)) contacts = contacts.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var phone = ContactValue.Phone(term);
            var lower = term.ToLowerInvariant();
            contacts = contacts.Where(c => c.DisplayName.Contains(term)
                || db.ContactMethods.Any(m => m.ContactId == c.Id &&
                    (m.NormalizedValue.Contains(lower) || (phone.Length >= 7 && m.NormalizedValue.Contains(phone)))));
        }

        var total = await contacts.CountAsync(ct);
        page = Math.Max(1, page);
        var rows = await (from c in contacts.OrderBy(c => c.DisplayName).Skip((page - 1) * pageSize).Take(pageSize)
                          join u in db.Users on c.OwnerUserId equals u.Id into us from u in us.DefaultIfEmpty()
                          select new
                          {
                              c.Id, c.Type, c.DisplayName, c.Status, c.CreatedAt,
                              Owner = u == null ? null : u.FirstName + " " + u.LastName,
                              Email = db.ContactMethods.Where(m => m.ContactId == c.Id && m.Kind == ContactMethodKinds.Email)
                                  .OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
                              Phone = db.ContactMethods.Where(m => m.ContactId == c.Id && (m.Kind == ContactMethodKinds.Mobile || m.Kind == ContactMethodKinds.Phone))
                                  .OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
                              Open = db.Opportunities.Count(o => o.PrimaryContactId == c.Id && o.Outcome == Outcomes.Open && o.ArchivedAt == null),
                          }).ToListAsync(ct);

        return new PagedResult<ContactRow>(
            rows.Select(r => new ContactRow(r.Id, r.Type, r.DisplayName, r.Status, r.Email, r.Phone, r.Owner, r.Open, r.CreatedAt)).ToList(),
            total, page, pageSize);
    }

    public async Task<ContactDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var contact = await (await scope.ContactsAsync(includeArchived: true)).Include(c => c.Methods).FirstOrDefaultAsync(c => c.Id == id, ct)
                      ?? throw new RecordNotFoundException("Contact");

        var owner = contact.OwnerUserId is null ? null
            : await db.Users.Where(u => u.Id == contact.OwnerUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);

        var props = await (from pc in db.PropertyContacts where pc.ContactId == id
                           join p in db.Properties on pc.PropertyId equals p.Id
                           orderby pc.EndedOn != null, pc.IsPrimary descending, p.Line1
                           select new { pc.Id, PropertyId = p.Id, p.Line1, p.Line2, p.City, p.State, p.PostalCode, pc.RoleKey, pc.IsPrimary, pc.EndedOn })
            .ToListAsync(ct);

        var orgs = await RelationsAsync(id, asPerson: true, ct);
        var people = contact.IsOrganization ? await RelationsAsync(id, asPerson: false, ct) : [];

        var opps = await OpportunityBriefsAsync((await scope.OpportunitiesAsync()).Where(o => o.PrimaryContactId == id), ct);

        return new ContactDetail(contact, owner, contact.Methods.OrderBy(m => m.Kind).ThenByDescending(m => m.IsPrimary).ToList(),
            props.Select(p => new ContactPropertyRow(p.Id, p.PropertyId,
                new Property { Line1 = p.Line1, Line2 = p.Line2, City = p.City, State = p.State, PostalCode = p.PostalCode }.SingleLine,
                p.RoleKey, p.IsPrimary, p.EndedOn == null)).ToList(),
            orgs, people, opps, await scope.CanEditAsync(contact.OwnerUserId),
            await db.ContactAddresses.Where(a => a.ContactId == id).OrderBy(a => a.Kind == ContactAddressKinds.Billing).ToListAsync(ct));
    }

    internal async Task<List<OpportunityBrief>> OpportunityBriefsAsync(IQueryable<Opportunity> opps, CancellationToken ct) =>
        (await (from o in opps
                join s in db.PipelineStages on o.StageId equals s.Id
                join p in db.Properties on o.PropertyId equals p.Id into ps from p in ps.DefaultIfEmpty()
                orderby o.CreatedAt descending
                select new { o.Id, o.Number, OppName = o.Name, StageName = s.Name, s.Color, o.Outcome, p, o.CreatedAt }).ToListAsync(ct))
        .Select(x => new OpportunityBrief(x.Id, x.Number, x.OppName, x.StageName, x.Color, x.Outcome, x.p?.SingleLine, x.CreatedAt)).ToList();

    private async Task<List<ContactRelationRow>> RelationsAsync(Guid contactId, bool asPerson, CancellationToken ct)
    {
        var rels = db.ContactRelationships.Where(r => r.EndedOn == null &&
            (asPerson ? r.PersonContactId == contactId : r.OrganizationContactId == contactId));
        var rows = await (from r in rels
                          join c in db.Contacts on (asPerson ? r.OrganizationContactId : r.PersonContactId) equals c.Id
                          where c.ArchivedAt == null
                          orderby r.IsPrimary descending, c.DisplayName
                          select new
                          {
                              r.Id, OtherId = c.Id, c.DisplayName, r.Title, r.IsPrimary,
                              Email = db.ContactMethods.Where(m => m.ContactId == c.Id && m.Kind == ContactMethodKinds.Email)
                                  .OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
                              Phone = db.ContactMethods.Where(m => m.ContactId == c.Id && (m.Kind == ContactMethodKinds.Mobile || m.Kind == ContactMethodKinds.Phone))
                                  .OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
                          }).ToListAsync(ct);
        return rows.Select(r => new ContactRelationRow(r.Id, r.OtherId, r.DisplayName, r.Title, r.IsPrimary, r.Email, r.Phone)).ToList();
    }

    public async Task<Guid> CreateAsync(ContactInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.RecordsCreate);
        // Null owner = the unassigned pool. Forms default the owner to the current user.
        var contact = new Contact { CreatedByUserId = scope.Me, OwnerUserId = input.OwnerUserId };
        Apply(contact, input);
        await EnsureOwnerIsMemberAsync(contact.OwnerUserId, ct);
        db.Contacts.Add(contact);

        if (input.OrganizationId is { } orgId && !contact.IsOrganization)
        {
            await EnsureOrganizationAsync(orgId, ct);
            db.ContactRelationships.Add(new ContactRelationship
            {
                PersonContactId = contact.Id, OrganizationContactId = orgId, Title = Clean(input.Title),
                IsPrimary = !await db.ContactRelationships.AnyAsync(r => r.OrganizationContactId == orgId && r.EndedOn == null, ct),
            });
        }

        await items.LogAsync(EntityTypes.Contact, contact.Id, "contact.created", $"{(contact.IsOrganization ? "Organization" : "Contact")} created", ct);
        await db.SaveChangesAsync(ct);
        return contact.Id;
    }

    public async Task UpdateAsync(Guid id, ContactInput input, CancellationToken ct = default)
    {
        var contact = await (await scope.ContactsAsync()).Include(c => c.Methods).FirstOrDefaultAsync(c => c.Id == id, ct)
                      ?? throw new RecordNotFoundException("Contact");
        await scope.EnsureCanEditAsync(contact.OwnerUserId);

        if (input.OwnerUserId != contact.OwnerUserId)
        {
            await scope.EnsurePermissionAsync(Permissions.RecordsReassign);
            await EnsureOwnerIsMemberAsync(input.OwnerUserId, ct);
            contact.OwnerUserId = input.OwnerUserId;
        }

        db.ContactMethods.RemoveRange(contact.Methods);
        contact.Methods.Clear();
        input.Type = contact.Type; // type is fixed after creation
        Apply(contact, input);
        // The new rows have their ids already, so EF would take them for existing rows being updated; they're new.
        foreach (var m in contact.Methods) db.Entry(m).State = EntityState.Added;
        await items.LogAsync(EntityTypes.Contact, contact.Id, "contact.updated", "Details updated", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.RecordsDelete);
        var contact = await (await scope.ContactsAsync()).FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new RecordNotFoundException("Contact");
        contact.ArchivedAt = DateTime.UtcNow;
        await items.LogAsync(EntityTypes.Contact, id, "contact.archived", "Archived", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddToOrganizationAsync(Guid personId, Guid organizationId, string? title, CancellationToken ct = default)
    {
        var person = await (await scope.ContactsAsync()).FirstOrDefaultAsync(c => c.Id == personId, ct) ?? throw new RecordNotFoundException("Contact");
        if (person.IsOrganization) throw new CrmRuleException("Only people can be added to an organization.");
        await EnsureOrganizationAsync(organizationId, ct);
        if (await db.ContactRelationships.AnyAsync(r => r.PersonContactId == personId && r.OrganizationContactId == organizationId && r.EndedOn == null, ct))
            throw new CrmRuleException("That person is already linked to this organization.");

        db.ContactRelationships.Add(new ContactRelationship
        {
            PersonContactId = personId, OrganizationContactId = organizationId, Title = Clean(title),
            IsPrimary = !await db.ContactRelationships.AnyAsync(r => r.OrganizationContactId == organizationId && r.EndedOn == null, ct),
        });
        await items.LogAsync(EntityTypes.Contact, organizationId, "contact.person_added", $"{person.DisplayName} added", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task EndRelationshipAsync(Guid relationshipId, CancellationToken ct = default)
    {
        var rel = await db.ContactRelationships.FirstOrDefaultAsync(r => r.Id == relationshipId, ct) ?? throw new RecordNotFoundException("Link");
        await scope.EnsureCanSeeAsync(EntityTypes.Contact, rel.OrganizationContactId);
        rel.EndedOn = DateOnly.FromDateTime(DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Other visible contacts that share an email or phone with the given values (duplicate warning).</summary>
    public async Task<List<(Guid Id, string Name)>> FindMatchesAsync(IEnumerable<MethodInput> methods, Guid? excludeId = null, CancellationToken ct = default)
    {
        var keys = methods.Where(m => !string.IsNullOrWhiteSpace(m.Value))
            .Select(m => ContactValue.Normalize(m.Kind, m.Value)).Where(v => v.Length >= 5).Distinct().ToList();
        if (keys.Count == 0) return [];
        var contacts = await scope.ContactsAsync();
        var rows = await contacts.Where(c => c.Id != excludeId && db.ContactMethods.Any(m => m.ContactId == c.Id && keys.Contains(m.NormalizedValue)))
            .Select(c => new { c.Id, c.DisplayName }).Take(5).ToListAsync(ct);
        return rows.Select(r => (r.Id, r.DisplayName)).ToList();
    }

    private static void Apply(Contact c, ContactInput input)
    {
        c.Type = input.Type == ContactTypes.Organization ? ContactTypes.Organization : ContactTypes.Person;
        c.FirstName = Clean(input.FirstName);
        c.LastName = Clean(input.LastName);
        c.OrganizationName = Clean(input.OrganizationName);
        c.SourceKey = Clean(input.SourceKey);
        c.CautionNote = Clean(input.CautionNote);
        c.Notes = Clean(input.Notes);
        c.RefreshDisplayName();
        if (c.DisplayName.Length == 0)
            throw new CrmRuleException(c.IsOrganization ? "Organization name is required." : "First or last name is required.");

        var methods = input.Methods.Where(m => !string.IsNullOrWhiteSpace(m.Value)).ToList();
        foreach (var kind in methods.Select(m => m.Kind).Distinct())
        {
            if (!ContactMethodKinds.All.Contains(kind)) throw new CrmRuleException($"Unknown contact method '{kind}'.");
            var ofKind = methods.Where(m => m.Kind == kind).ToList();
            var primary = ofKind.FirstOrDefault(m => m.IsPrimary) ?? ofKind[0];
            foreach (var m in ofKind)
            {
                var value = m.Value.Trim();
                if (kind == ContactMethodKinds.Email && !value.Contains('@')) throw new CrmRuleException($"'{value}' is not an email address.");
                c.Methods.Add(new ContactMethod
                {
                    ContactId = c.Id, Kind = kind, Value = value, NormalizedValue = ContactValue.Normalize(kind, value),
                    Label = Clean(m.Label), IsPrimary = ReferenceEquals(m, primary),
                });
            }
        }
    }

    private async Task EnsureOrganizationAsync(Guid orgId, CancellationToken ct)
    {
        var org = await (await scope.ContactsAsync()).FirstOrDefaultAsync(c => c.Id == orgId, ct) ?? throw new RecordNotFoundException("Organization");
        if (!org.IsOrganization) throw new CrmRuleException("That contact is not an organization.");
    }

    internal async Task EnsureOwnerIsMemberAsync(Guid? ownerUserId, CancellationToken ct)
    {
        if (ownerUserId is null) return;
        if (!await db.Memberships.AnyAsync(m => m.UserId == ownerUserId && m.Status == Domain.Tenancy.MembershipStatus.Active, ct))
            throw new CrmRuleException("The owner must be an active user in this company.");
    }

    /// <summary>Sets the contact's mailing or billing address (one of each).</summary>
    public async Task SaveAddressAsync(Guid contactId, string kind, AddressInput input, CancellationToken ct = default)
    {
        var contact = await (await scope.ContactsAsync()).FirstOrDefaultAsync(c => c.Id == contactId, ct) ?? throw new RecordNotFoundException("Contact");
        await scope.EnsureCanEditAsync(contact.OwnerUserId);
        if (!ContactAddressKinds.All.Contains(kind)) throw new CrmRuleException("Pick mailing or billing.");
        var a = await db.ContactAddresses.FirstOrDefaultAsync(x => x.ContactId == contactId && x.Kind == kind, ct);
        if (a is null) { a = new ContactAddress { ContactId = contactId, Kind = kind }; db.ContactAddresses.Add(a); }
        a.Line1 = Required(input.Line1, "Street address");
        a.Line2 = Clean(input.Line2);
        a.City = Required(input.City, "City");
        a.State = Required(input.State, "State").ToUpperInvariant();
        a.PostalCode = Required(input.PostalCode, "ZIP code");
        await items.LogAsync(EntityTypes.Contact, contactId, "contact.address_saved", $"{(kind == ContactAddressKinds.Billing ? "Billing" : "Mailing")} address: {a.SingleLine}", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAddressAsync(Guid contactId, string kind, CancellationToken ct = default)
    {
        var contact = await (await scope.ContactsAsync()).FirstOrDefaultAsync(c => c.Id == contactId, ct) ?? throw new RecordNotFoundException("Contact");
        await scope.EnsureCanEditAsync(contact.OwnerUserId);
        var a = await db.ContactAddresses.FirstOrDefaultAsync(x => x.ContactId == contactId && x.Kind == kind, ct);
        if (a is null) return;
        db.ContactAddresses.Remove(a);
        await items.LogAsync(EntityTypes.Contact, contactId, "contact.address_removed", $"{(kind == ContactAddressKinds.Billing ? "Billing" : "Mailing")} address removed", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Where to send mail: the mailing address, else billing, else their primary current property.</summary>
    public async Task<string?> MailingLineAsync(Guid contactId, CancellationToken ct = default)
    {
        var own = await db.ContactAddresses.Where(a => a.ContactId == contactId).OrderBy(a => a.Kind == ContactAddressKinds.Billing).FirstOrDefaultAsync(ct);
        if (own is not null) return own.SingleLine;
        var p = await (from pc in db.PropertyContacts where pc.ContactId == contactId && pc.EndedOn == null
                       join pr in db.Properties on pc.PropertyId equals pr.Id
                       orderby pc.IsPrimary descending, pc.CreatedAt descending select pr).FirstOrDefaultAsync(ct);
        return p?.SingleLine;
    }

    private static string Required(string? s, string label) =>
        string.IsNullOrWhiteSpace(s) ? throw new CrmRuleException($"{label} is required.") : s.Trim();

    internal static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
