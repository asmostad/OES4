using Microsoft.EntityFrameworkCore;
using Oes.Domain.Crm;
using Oes.Domain.Tenancy;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

public sealed record Option(string Value, string Label);

public sealed record SearchHit(string Kind, Guid Id, string Title, string? Subtitle, string Url);

/// <summary>Pick lists for forms, plus global search across the records the user can see.</summary>
public sealed class CrmLookups(OesDbContext db, RecordScope scope)
{
    private readonly Dictionary<string, List<Option>> _cache = [];

    public async Task<List<Option>> ValuesAsync(string type, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(type, out var hit)) return hit;
        return _cache[type] = await db.LookupValues.Where(l => l.Type == type && l.IsActive).OrderBy(l => l.Sort)
            .Select(l => new Option(l.Key, l.Label)).ToListAsync(ct);
    }

    public async Task<string?> LabelAsync(string type, string? key, CancellationToken ct = default) =>
        key is null ? null : (await ValuesAsync(type, ct)).FirstOrDefault(o => o.Value == key)?.Label ?? key;

    // Guid.ToString() must run in .NET, not SQL: IDs are BINARY(16) in the database.
    public async Task<List<Option>> UsersAsync(CancellationToken ct = default) =>
        (await (from m in db.Memberships where m.Status == MembershipStatus.Active
                join u in db.Users on m.UserId equals u.Id
                orderby u.FirstName, u.LastName
                select new { u.Id, Name = u.FirstName + " " + u.LastName }).ToListAsync(ct))
        .Select(u => new Option(u.Id.ToString(), u.Name)).ToList();

    public async Task<List<Option>> OrganizationsAsync(string? q, CancellationToken ct = default)
    {
        var orgs = (await scope.ContactsAsync()).Where(c => c.Type == ContactTypes.Organization);
        if (!string.IsNullOrWhiteSpace(q)) orgs = orgs.Where(c => c.DisplayName.Contains(q.Trim()));
        return (await orgs.OrderBy(c => c.DisplayName).Take(20).Select(c => new { c.Id, c.DisplayName }).ToListAsync(ct))
            .Select(c => new Option(c.Id.ToString(), c.DisplayName)).ToList();
    }

    public async Task<List<SearchHit>> SearchAsync(string q, int perKind = 5, CancellationToken ct = default)
    {
        q = q.Trim();
        if (q.Length < 2) return [];
        var lower = q.ToLowerInvariant();
        var phone = ContactValue.Phone(q);
        var hits = new List<SearchHit>();

        var contacts = await (await scope.ContactsAsync())
            .Where(c => c.DisplayName.Contains(q) || db.ContactMethods.Any(m => m.ContactId == c.Id &&
                (m.NormalizedValue.Contains(lower) || (phone.Length >= 7 && m.NormalizedValue.Contains(phone)))))
            .OrderBy(c => c.DisplayName).Take(perKind)
            .Select(c => new { c.Id, c.DisplayName, c.Type, c.Status }).ToListAsync(ct);
        hits.AddRange(contacts.Select(c => new SearchHit(c.Type == ContactTypes.Organization ? "organization" : "contact", c.Id,
            c.DisplayName, c.Type == ContactTypes.Organization ? "Organization" : Cap(c.Status), $"/Contacts/View/{c.Id}")));

        var key = AddressKey.NormalizeLine(q);
        var props = await (await scope.PropertiesAsync())
            .Where(p => p.Line1.Contains(q) || p.City.Contains(q) || p.PostalCode.StartsWith(q) || (key.Length > 1 && p.NormalizedKey.Contains(key)))
            .OrderBy(p => p.Line1).Take(perKind).ToListAsync(ct);
        hits.AddRange(props.Select(p => new SearchHit("property", p.Id, p.Line1 + (p.Line2 is null ? "" : " " + p.Line2), p.CityLine, $"/Properties/View/{p.Id}")));

        var opps = await (from o in await scope.OpportunitiesAsync()
                          join c in db.Contacts on o.PrimaryContactId equals c.Id
                          where o.Number.Contains(q) || o.Name.Contains(q)
                          orderby o.CreatedAt descending
                          select new { o.Id, o.Number, o.Name, c.DisplayName }).Take(perKind).ToListAsync(ct);
        hits.AddRange(opps.Select(o => new SearchHit("opportunity", o.Id, o.Name, $"{o.Number} · {o.DisplayName}", $"/Opportunities/View/{o.Id}")));

        return hits;
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' ');
}
