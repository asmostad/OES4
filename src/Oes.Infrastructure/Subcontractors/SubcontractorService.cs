using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Domain.Measurements;
using Oes.Domain.Subcontractors;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Subcontractors;

public sealed record SubRow(Guid Id, Guid ContactId, string Name, string? Email, string? Phone, List<string> TradeNames, string Status,
    DateOnly? InsuranceExpiresOn, int OpenWorkOrders)
{
    public int? InsuranceDaysLeft => InsuranceExpiresOn is { } d ? d.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber : null;
}

public sealed record SubDocRow(SubcontractorDocument Doc, string FileName);

public sealed record SubDetail(Subcontractor Sub, Contact Contact, List<TradeType> Trades, List<SubDocRow> Documents,
    List<SubcontractorRate> Rates, List<ContactRelationRow> Crew, bool CanManage)
{
    public List<SubcontractorRate> CurrentRates => Rates.Where(r => r.EffectiveTo is null).OrderBy(r => r.Description).ToList();
}

public sealed class NewSubInput
{
    public Guid? ContactId { get; set; }
    public string? OrganizationName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public List<string> TradeCodes { get; set; } = [];
}

/// <summary>Subcontractor profiles on organization contacts, with documents and effective-dated rates.</summary>
public sealed class SubcontractorService(OesDbContext db, RecordScope scope, RecordItems items, ContactService contacts, ICurrentUser user, IClock clock)
{
    private async Task EnsureCanViewAsync()
    {
        if (!await user.HasPermissionAsync(Permissions.SubcontractorsManage) && !await user.HasPermissionAsync(Permissions.WorkOrdersManage))
            throw new RecordForbiddenException("You don't have access to subcontractors.");
    }

    public async Task<List<SubRow>> ListAsync(string? q, string? tradeCode, bool activeOnly, CancellationToken ct = default)
    {
        await EnsureCanViewAsync();
        var subs = db.Subcontractors.AsQueryable();
        if (activeOnly) subs = subs.Where(s => s.Status == SubStatuses.Active);
        if (!string.IsNullOrWhiteSpace(tradeCode)) subs = subs.Where(s => s.TradeCodes != null && s.TradeCodes.Contains(tradeCode));
        var rows = await (from s in subs
                          join c in db.Contacts on s.ContactId equals c.Id
                          where q == null || c.DisplayName.Contains(q)
                          orderby c.DisplayName
                          select new
                          {
                              s, c.DisplayName,
                              Email = db.ContactMethods.Where(m => m.ContactId == c.Id && m.Kind == ContactMethodKinds.Email).OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
                              Phone = db.ContactMethods.Where(m => m.ContactId == c.Id && (m.Kind == ContactMethodKinds.Mobile || m.Kind == ContactMethodKinds.Phone)).OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault(),
                              Open = db.WorkOrders.Count(w => w.SubcontractorId == s.Id && (w.Status == WorkOrderStatuses.Draft || w.Status == WorkOrderStatuses.Issued)),
                          }).ToListAsync(ct);
        var trades = await db.TradeTypes.ToDictionaryAsync(t => t.Code, t => t.Name, ct);
        return rows.Select(r => new SubRow(r.s.Id, r.s.ContactId, r.DisplayName, r.Email, r.Phone,
            r.s.Trades.Select(code => trades.GetValueOrDefault(code, code)).ToList(), r.s.Status, r.s.InsuranceExpiresOn, r.Open)).ToList();
    }

    public async Task<SubDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureCanViewAsync();
        var sub = await db.Subcontractors.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new RecordNotFoundException("Subcontractor");
        var contact = await db.Contacts.Include(c => c.Methods).FirstAsync(c => c.Id == sub.ContactId, ct);
        var docs = await (from d in db.SubcontractorDocuments where d.SubcontractorId == id
                          join doc in db.Documents on d.DocumentId equals doc.Id
                          where doc.ArchivedAt == null
                          orderby d.DocTypeKey, d.ExpiresOn descending
                          select new SubDocRow(d, doc.FileName)).ToListAsync(ct);
        var rates = await db.SubcontractorRates.Where(r => r.SubcontractorId == id).OrderByDescending(r => r.EffectiveFrom).ToListAsync(ct);
        var crew = await (from r in db.ContactRelationships where r.OrganizationContactId == contact.Id && r.EndedOn == null
                          join c in db.Contacts on r.PersonContactId equals c.Id
                          select new ContactRelationRow(r.Id, c.Id, c.DisplayName, r.Title, r.IsPrimary, null, null)).ToListAsync(ct);
        return new SubDetail(sub, contact, await db.TradeTypes.Where(t => t.IsActive).OrderBy(t => t.Sort).ToListAsync(ct), docs, rates, crew,
            await user.HasPermissionAsync(Permissions.SubcontractorsManage, ct));
    }

    public async Task<Guid> CreateAsync(NewSubInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SubcontractorsManage);
        Guid contactId;
        if (input.ContactId is { } existing)
        {
            var c = await db.Contacts.FirstOrDefaultAsync(x => x.Id == existing && x.ArchivedAt == null, ct) ?? throw new RecordNotFoundException("Contact");
            if (!c.IsOrganization) throw new CrmRuleException("Subcontractors are organizations. Add the person to an organization first.");
            if (await db.Subcontractors.AnyAsync(s => s.ContactId == existing, ct)) throw new CrmRuleException("That organization is already a subcontractor.");
            contactId = existing;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.OrganizationName)) throw new CrmRuleException("Enter the company name or pick an existing organization.");
            // Subs are shared company records: unassigned, so every user who schedules work can see them.
            contactId = await contacts.CreateAsync(new ContactInput
            {
                Type = ContactTypes.Organization, OrganizationName = input.OrganizationName, OwnerUserId = null, SourceKey = null,
                Methods =
                [
                    .. string.IsNullOrWhiteSpace(input.Email) ? Array.Empty<MethodInput>() : [new MethodInput(ContactMethodKinds.Email, input.Email, null, true)],
                    .. string.IsNullOrWhiteSpace(input.Phone) ? Array.Empty<MethodInput>() : [new MethodInput(ContactMethodKinds.Mobile, input.Phone, null, true)],
                ],
            }, ct);
        }
        var sub = new Subcontractor { ContactId = contactId, TradeCodes = await CleanTradesAsync(input.TradeCodes, ct) };
        db.Subcontractors.Add(sub);
        await items.LogAsync(EntityTypes.Contact, contactId, "subcontractor.created", "Set up as a subcontractor", ct);
        await db.SaveChangesAsync(ct);
        return sub.Id;
    }

    public async Task UpdateProfileAsync(Guid id, List<string> tradeCodes, string status, string? notes, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SubcontractorsManage);
        if (!SubStatuses.All.Contains(status)) throw new CrmRuleException("Unknown status.");
        var sub = await db.Subcontractors.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new RecordNotFoundException("Subcontractor");
        sub.TradeCodes = await CleanTradesAsync(tradeCodes, ct);
        sub.Status = status;
        sub.Notes = ContactService.Clean(notes);
        await db.SaveChangesAsync(ct);
    }

    private async Task<string?> CleanTradesAsync(IEnumerable<string> codes, CancellationToken ct)
    {
        var valid = await db.TradeTypes.Select(t => t.Code).ToListAsync(ct);
        var list = codes.Where(valid.Contains).Distinct().ToList();
        return list.Count == 0 ? null : string.Join(",", list);
    }

    public async Task AddDocumentAsync(Guid id, string docTypeKey, DateOnly? expiresOn, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SubcontractorsManage);
        if (!SubDocTypes.All.Contains(docTypeKey)) throw new CrmRuleException("Unknown document type.");
        if (SubDocTypes.Insurance.Contains(docTypeKey) && expiresOn is null) throw new CrmRuleException("Insurance certificates need an expiry date.");
        var sub = await db.Subcontractors.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new RecordNotFoundException("Subcontractor");
        var docId = await items.UploadAsync(EntityTypes.Contact, sub.ContactId, content, fileName, contentType, "insurance", ct);
        db.SubcontractorDocuments.Add(new SubcontractorDocument { SubcontractorId = id, DocTypeKey = docTypeKey, DocumentId = docId, ExpiresOn = expiresOn });
        await db.SaveChangesAsync(ct);
        await RefreshInsuranceAsync(sub, ct);
    }

    /// <summary>The earliest expiry among the latest certificate of each insurance type.</summary>
    private async Task RefreshInsuranceAsync(Subcontractor sub, CancellationToken ct)
    {
        var docs = await db.SubcontractorDocuments.Where(d => d.SubcontractorId == sub.Id && SubDocTypes.Insurance.Contains(d.DocTypeKey) && d.ExpiresOn != null).ToListAsync(ct);
        sub.InsuranceExpiresOn = docs.GroupBy(d => d.DocTypeKey).Select(g => g.Max(d => d.ExpiresOn)).Min();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds a rate. A current rate with the same trade, description and unit is ended the day before (never edited).</summary>
    public async Task AddRateAsync(Guid id, Guid? tradeTypeId, string description, string unit, decimal rate, DateOnly effectiveFrom, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.SubcontractorsManage);
        if (string.IsNullOrWhiteSpace(description)) throw new CrmRuleException("Describe what the rate is for.");
        if (rate < 0) throw new CrmRuleException("Rates can't be negative.");
        if (!await db.Subcontractors.AnyAsync(s => s.Id == id, ct)) throw new RecordNotFoundException("Subcontractor");
        description = description.Trim();
        unit = string.IsNullOrWhiteSpace(unit) ? "EA" : unit.Trim().ToUpperInvariant();
        var previous = await db.SubcontractorRates.Where(r => r.SubcontractorId == id && r.EffectiveTo == null && r.TradeTypeId == tradeTypeId
                                                              && r.Description == description && r.Unit == unit).ToListAsync(ct);
        foreach (var p in previous)
        {
            if (p.EffectiveFrom >= effectiveFrom) throw new CrmRuleException($"The current rate already starts on {p.EffectiveFrom:MMM d, yyyy}. Pick a later date.");
            p.EffectiveTo = effectiveFrom.AddDays(-1);
        }
        db.SubcontractorRates.Add(new SubcontractorRate
        {
            SubcontractorId = id, TradeTypeId = tradeTypeId, Description = description, Unit = unit, Rate = rate, EffectiveFrom = effectiveFrom,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Rates in effect on a date (for work order lines).</summary>
    public async Task<List<SubcontractorRate>> RatesOnAsync(Guid id, DateOnly on, CancellationToken ct = default) =>
        await db.SubcontractorRates.Where(r => r.SubcontractorId == id && r.EffectiveFrom <= on && (r.EffectiveTo == null || r.EffectiveTo >= on))
            .OrderBy(r => r.Description).ToListAsync(ct);

    public DateOnly Today => DateOnly.FromDateTime(clock.UtcNow);
}
