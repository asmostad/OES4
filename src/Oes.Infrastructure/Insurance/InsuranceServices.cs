using Microsoft.EntityFrameworkCore;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Domain.Insurance;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Insurance;

/// <summary>A carrier as the tenant sees it: the platform row with the tenant's overrides applied.</summary>
public sealed record EffectiveCarrier(
    Guid? TenantCarrierId, Guid? CarrierId, string Name, string? Phone, string? ClaimsEmail, string? ClaimsFax,
    string? Website, string? PreferredSubmission, string? Address, string? Notes, bool IsHidden, bool IsCustom, bool HasOverrides);

public sealed class CarrierInput
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? ClaimsEmail { get; set; }
    public string? ClaimsFax { get; set; }
    public string? Website { get; set; }
    public string? PreferredSubmission { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Platform default carriers merged with each tenant's copies (decision 5). Editing a platform
/// carrier creates or updates the tenant's TenantCarrier row; the platform row never changes.
/// </summary>
public sealed class CarrierService(OesDbContext db, RecordScope scope, Oes.Application.Abstractions.IAuditLog audit)
{
    public async Task<List<EffectiveCarrier>> ListAsync(bool includeHidden = false, CancellationToken ct = default)
    {
        var platform = await db.Carriers.Where(c => c.IsActive).ToListAsync(ct);
        var mine = await db.TenantCarriers.ToListAsync(ct);
        var result = new List<EffectiveCarrier>();

        foreach (var p in platform)
        {
            var t = mine.FirstOrDefault(m => m.CarrierId == p.Id);
            result.Add(Merge(p, t));
        }
        result.AddRange(mine.Where(m => m.CarrierId is null).Select(t => Merge(null, t)));
        return result.Where(c => includeHidden || !c.IsHidden).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<EffectiveCarrier?> GetByTenantCarrierAsync(Guid? tenantCarrierId, CancellationToken ct = default)
    {
        if (tenantCarrierId is null) return null;
        var t = await db.TenantCarriers.FirstOrDefaultAsync(x => x.Id == tenantCarrierId, ct);
        if (t is null) return null;
        var p = t.CarrierId is null ? null : await db.Carriers.FirstOrDefaultAsync(c => c.Id == t.CarrierId, ct);
        return Merge(p, t);
    }

    /// <summary>
    /// Returns the tenant's row for a carrier choice, creating an empty one for a platform
    /// carrier the tenant hasn't touched yet. Claims always point at the tenant's row.
    /// The choice is "t:{tenantCarrierId}" or "p:{carrierId}".
    /// </summary>
    public async Task<Guid?> ResolveChoiceAsync(string? choice, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(choice)) return null;
        if (choice.StartsWith("t:") && Guid.TryParse(choice[2..], out var tid))
            return await db.TenantCarriers.AnyAsync(t => t.Id == tid, ct) ? tid : throw new CrmRuleException("Unknown carrier.");
        if (choice.StartsWith("p:") && Guid.TryParse(choice[2..], out var pid))
        {
            if (!await db.Carriers.AnyAsync(c => c.Id == pid && c.IsActive, ct)) throw new CrmRuleException("Unknown carrier.");
            var existing = await db.TenantCarriers.FirstOrDefaultAsync(t => t.CarrierId == pid, ct);
            if (existing is not null) return existing.Id;
            var row = new TenantCarrier { CarrierId = pid };
            db.TenantCarriers.Add(row);
            await db.SaveChangesAsync(ct);
            return row.Id;
        }
        throw new CrmRuleException("Unknown carrier.");
    }

    public static string ChoiceFor(EffectiveCarrier c) => c.TenantCarrierId is { } t ? $"t:{t}" : $"p:{c.CarrierId}";

    /// <summary>Saves the tenant's version of a platform carrier (overrides only; blank = use default).</summary>
    public async Task SaveOverridesAsync(Guid carrierId, CarrierInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.CarriersManage);
        var platform = await db.Carriers.FirstOrDefaultAsync(c => c.Id == carrierId, ct) ?? throw new RecordNotFoundException("Carrier");
        var row = await db.TenantCarriers.FirstOrDefaultAsync(t => t.CarrierId == carrierId, ct);
        if (row is null) { row = new TenantCarrier { CarrierId = carrierId }; db.TenantCarriers.Add(row); }
        Apply(row, input, platform);
        audit.Record("carrier.updated", "Carrier", row.Id, $"Edited {platform.Name}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> SaveCustomAsync(Guid? tenantCarrierId, CarrierInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.CarriersManage);
        if (string.IsNullOrWhiteSpace(input.Name)) throw new CrmRuleException("Carrier name is required.");
        TenantCarrier row;
        if (tenantCarrierId is { } id)
        {
            row = await db.TenantCarriers.FirstOrDefaultAsync(t => t.Id == id && t.CarrierId == null, ct) ?? throw new RecordNotFoundException("Carrier");
        }
        else
        {
            row = new TenantCarrier();
            db.TenantCarriers.Add(row);
        }
        Apply(row, input, null);
        audit.Record(tenantCarrierId is null ? "carrier.created" : "carrier.updated", "Carrier", row.Id, $"Carrier {row.Name}");
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    public async Task SetHiddenAsync(string choice, bool hidden, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.CarriersManage);
        var id = await ResolveChoiceAsync(choice, ct) ?? throw new RecordNotFoundException("Carrier");
        var row = await db.TenantCarriers.FirstAsync(t => t.Id == id, ct);
        row.IsHidden = hidden;
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(TenantCarrier row, CarrierInput i, Carrier? platform)
    {
        // For platform carriers, a value equal to the default is stored as null so later platform fixes still apply.
        string? Ov(string? value, string? fallback) =>
            string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), fallback, StringComparison.Ordinal) ? null : value.Trim();
        row.Name = platform is null ? i.Name!.Trim() : Ov(i.Name, platform.Name);
        row.Phone = Ov(i.Phone, platform?.Phone);
        row.ClaimsEmail = Ov(i.ClaimsEmail, platform?.ClaimsEmail);
        row.ClaimsFax = Ov(i.ClaimsFax, platform?.ClaimsFax);
        row.Website = Ov(i.Website, platform?.Website);
        row.PreferredSubmission = Ov(i.PreferredSubmission, platform?.PreferredSubmission);
        row.Address = Ov(i.Address, platform?.Address);
        row.Notes = string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim();
    }

    private static EffectiveCarrier Merge(Carrier? p, TenantCarrier? t) => new(
        t?.Id, p?.Id ?? t?.CarrierId,
        t?.Name ?? p?.Name ?? "",
        t?.Phone ?? p?.Phone, t?.ClaimsEmail ?? p?.ClaimsEmail, t?.ClaimsFax ?? p?.ClaimsFax,
        t?.Website ?? p?.Website, t?.PreferredSubmission ?? p?.PreferredSubmission, t?.Address ?? p?.Address,
        t?.Notes, t?.IsHidden ?? false, p is null,
        t is not null && (t.Name ?? t.Phone ?? t.ClaimsEmail ?? t.ClaimsFax ?? t.Website ?? t.PreferredSubmission ?? t.Address ?? t.Notes) is not null);
}

public sealed class StormInput
{
    public string Name { get; set; } = "";
    public DateOnly EventDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string PerilKey { get; set; } = "hail";
    public decimal? MaxHailSizeIn { get; set; }
    public int? MaxWindMph { get; set; }
    public string? ZipCodes { get; set; }
    public string? Notes { get; set; }
}

public sealed record StormRow(Guid Id, string Name, DateOnly EventDate, string PerilKey, decimal? MaxHailSizeIn, int? MaxWindMph,
    int ZipCount, int Opportunities, int Claims, decimal? PipelineValue);

public sealed class StormService(OesDbContext db, RecordScope scope)
{
    public async Task<List<StormRow>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var opps = await scope.OpportunitiesAsync();
        var claims = await scope.ClaimsAsync();
        var storms = db.StormEvents.AsQueryable();
        if (!includeArchived) storms = storms.Where(s => s.ArchivedAt == null);
        var rows = await storms.OrderByDescending(s => s.EventDate)
            .Select(s => new
            {
                s,
                Opps = opps.Count(o => o.StormEventId == s.Id),
                Claims = claims.Count(c => c.StormEventId == s.Id),
                Value = opps.Where(o => o.StormEventId == s.Id && o.Outcome != Outcomes.Lost).Sum(o => o.ExpectedValue),
            }).ToListAsync(ct);
        return rows.Select(r => new StormRow(r.s.Id, r.s.Name, r.s.EventDate, r.s.PerilKey, r.s.MaxHailSizeIn, r.s.MaxWindMph,
            r.s.Zips.Count(), r.Opps, r.Claims, r.Value)).ToList();
    }

    public async Task<StormEvent> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.StormEvents.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new RecordNotFoundException("Storm");

    public async Task<Guid> SaveAsync(Guid? id, StormInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.StormsManage);
        if (string.IsNullOrWhiteSpace(input.Name)) throw new CrmRuleException("Storm name is required.");
        if (input.MaxHailSizeIn is < 0 or > 10) throw new CrmRuleException("Hail size should be in inches (0–10).");
        var storm = id is { } sid ? await GetAsync(sid, ct) : new StormEvent();
        if (id is null) db.StormEvents.Add(storm);
        storm.Name = input.Name.Trim();
        storm.EventDate = input.EventDate;
        storm.PerilKey = string.IsNullOrWhiteSpace(input.PerilKey) ? "hail" : input.PerilKey;
        storm.MaxHailSizeIn = input.MaxHailSizeIn;
        storm.MaxWindMph = input.MaxWindMph;
        storm.ZipCodes = NormalizeZips(input.ZipCodes);
        storm.Notes = ContactService.Clean(input.Notes);
        await db.SaveChangesAsync(ct);
        return storm.Id;
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.StormsManage);
        var storm = await GetAsync(id, ct);
        storm.ArchivedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Visible properties inside the storm's ZIP codes that have no opportunity on this storm yet (canvass list).</summary>
    public async Task<List<PropertyRow>> UntouchedPropertiesAsync(Guid stormId, int take = 100, CancellationToken ct = default) =>
        (await UntouchedAsync(stormId, take, ct)).Select(p => new PropertyRow(p.Id, p.Line1, p.Line2, p.CityLine, p.Kind, null, 0, p.CreatedAt)).ToList();

    /// <summary>The same canvass list as properties (with their map position, for sorting by distance in the field).</summary>
    public async Task<List<Property>> UntouchedAsync(Guid stormId, int take = 100, CancellationToken ct = default)
    {
        var storm = await GetAsync(stormId, ct);
        var zips = storm.Zips.Select(z => z.Length > 5 ? z[..5] : z).ToList();
        if (zips.Count == 0) return [];
        var props = await scope.PropertiesAsync();
        return await props.Where(p => zips.Contains(p.PostalCode.Substring(0, 5))
                                     && !db.Opportunities.Any(o => o.PropertyId == p.Id && o.StormEventId == stormId))
            .OrderBy(p => p.PostalCode).ThenBy(p => p.Line1).Take(take).ToListAsync(ct);
    }

    /// <summary>Suggests the storm for a property's ZIP and a date of loss (same ZIP, within 3 days).</summary>
    public async Task<StormEvent?> SuggestAsync(string? postalCode, DateOnly? dateOfLoss, CancellationToken ct = default)
    {
        var zip = AddressKey.Zip5(postalCode);
        if (zip.Length != 5) return null;
        var q = db.StormEvents.Where(s => s.ArchivedAt == null && s.ZipCodes != null && s.ZipCodes.Contains(zip));
        if (dateOfLoss is { } d) q = q.Where(s => s.EventDate >= d.AddDays(-3) && s.EventDate <= d.AddDays(3));
        return await q.OrderByDescending(s => s.EventDate).FirstOrDefaultAsync(ct);
    }

    private static string? NormalizeZips(string? zips)
    {
        if (string.IsNullOrWhiteSpace(zips)) return null;
        var list = zips.Split([',', ' ', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(AddressKey.Zip5).Where(z => z.Length == 5).Distinct().Order().ToList();
        return list.Count == 0 ? null : string.Join(", ", list);
    }
}

public sealed class ClaimInput
{
    public string? CarrierChoice { get; set; }
    public string? ClaimNumber { get; set; }
    public string? PolicyNumber { get; set; }
    public DateOnly? DateOfLoss { get; set; }
    public string? PerilKey { get; set; }
    public decimal? Deductible { get; set; }
    public Guid? AdjusterContactId { get; set; }
    public Guid? DeskAdjusterContactId { get; set; }
    public Guid? StormEventId { get; set; }
    public string Status { get; set; } = ClaimStatuses.NotFiled;
    public DateOnly? FiledOn { get; set; }
    public DateOnly? InspectionOn { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string? Notes { get; set; }
}

public sealed record ClaimRow(Guid Id, string? ClaimNumber, string Status, string? CarrierName, Guid OpportunityId, string OpportunityNumber,
    string OpportunityName, string ContactName, string? Address, DateOnly? DateOfLoss, string? StormName, DateOnly? InspectionOn,
    decimal? ApprovedAmount, int OpenSupplements);

public sealed record SupplementRow(Supplement Supplement, string? SubmittedBy);

public sealed record ClaimDetail(
    InsuranceClaim Claim, Opportunity Opportunity, Contact Contact, Property? Property, EffectiveCarrier? Carrier,
    Contact? Adjuster, Contact? DeskAdjuster, StormEvent? Storm, List<SupplementRow> Supplements, bool CanEdit)
{
    public decimal SupplementsApproved => Supplements.Sum(s => s.Supplement.ApprovedAmount ?? 0);
    public decimal? TotalApproved => Claim.ApprovedAmount is null && SupplementsApproved == 0 ? null : (Claim.ApprovedAmount ?? 0) + SupplementsApproved;
}

public sealed class SupplementInput
{
    public string Reason { get; set; } = "";
    public string Status { get; set; } = SupplementStatuses.Needed;
    public decimal? RequestedAmount { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public DateOnly? SentOn { get; set; }
    public DateOnly? RespondedOn { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Insurance claims and supplements. Visibility and edit rights follow the opportunity.</summary>
public sealed class ClaimService(OesDbContext db, RecordScope scope, RecordItems items, CarrierService carriers)
{
    public async Task<List<ClaimRow>> ListAsync(string? q, string? status, Guid? stormId, string? carrierChoice, CancellationToken ct = default)
    {
        var claims = await scope.ClaimsAsync();
        if (!string.IsNullOrWhiteSpace(status)) claims = claims.Where(c => c.Status == status);
        if (stormId is { } sid) claims = claims.Where(c => c.StormEventId == sid);
        if (!string.IsNullOrWhiteSpace(carrierChoice) && carrierChoice.StartsWith("t:") && Guid.TryParse(carrierChoice[2..], out var tcid))
            claims = claims.Where(c => c.TenantCarrierId == tcid);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            claims = claims.Where(c => (c.ClaimNumber != null && c.ClaimNumber.Contains(t)) || (c.PolicyNumber != null && c.PolicyNumber.Contains(t))
                || db.Opportunities.Any(o => o.Id == c.OpportunityId && (o.Number.Contains(t) || o.Name.Contains(t)
                    || db.Contacts.Any(x => x.Id == o.PrimaryContactId && x.DisplayName.Contains(t)))));
        }

        var raw = await (from c in claims
                         join o in db.Opportunities on c.OpportunityId equals o.Id
                         join k in db.Contacts on o.PrimaryContactId equals k.Id
                         join p in db.Properties on c.PropertyId equals p.Id into ps from p in ps.DefaultIfEmpty()
                         join s in db.StormEvents on c.StormEventId equals s.Id into ss from s in ss.DefaultIfEmpty()
                         orderby c.CreatedAt descending
                         select new
                         {
                             c, o.Number, OppName = o.Name, k.DisplayName, p, StormName = s == null ? null : s.Name,
                             Open = db.Supplements.Count(x => x.ClaimId == c.Id && (x.Status == SupplementStatuses.Needed || x.Status == SupplementStatuses.Sent)),
                         }).Take(500).ToListAsync(ct);

        var carrierNames = (await carriers.ListAsync(includeHidden: true, ct)).Where(x => x.TenantCarrierId is not null)
            .ToDictionary(x => x.TenantCarrierId!.Value, x => x.Name);
        return raw.Select(r => new ClaimRow(r.c.Id, r.c.ClaimNumber, r.c.Status,
            r.c.TenantCarrierId is { } id ? carrierNames.GetValueOrDefault(id) : null, r.c.OpportunityId, r.Number, r.OppName, r.DisplayName,
            r.p?.SingleLine, r.c.DateOfLoss, r.StormName, r.c.InspectionOn, r.c.ApprovedAmount, r.Open)).ToList();
    }

    public async Task<ClaimDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var claim = await (await scope.ClaimsAsync(includeArchived: true)).FirstOrDefaultAsync(c => c.Id == id, ct)
                    ?? throw new RecordNotFoundException("Claim");
        var opp = await db.Opportunities.FirstAsync(o => o.Id == claim.OpportunityId, ct);
        var contact = await db.Contacts.FirstAsync(c => c.Id == opp.PrimaryContactId, ct);
        var property = claim.PropertyId is null ? null : await db.Properties.FirstOrDefaultAsync(p => p.Id == claim.PropertyId, ct);
        var adjuster = claim.AdjusterContactId is null ? null : await db.Contacts.Include(c => c.Methods).FirstOrDefaultAsync(c => c.Id == claim.AdjusterContactId, ct);
        var desk = claim.DeskAdjusterContactId is null ? null : await db.Contacts.Include(c => c.Methods).FirstOrDefaultAsync(c => c.Id == claim.DeskAdjusterContactId, ct);
        var storm = claim.StormEventId is null ? null : await db.StormEvents.FirstOrDefaultAsync(s => s.Id == claim.StormEventId, ct);
        var supplements = await (from s in db.Supplements where s.ClaimId == id
                                 join u in db.Users on s.SubmittedByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                                 orderby s.CreatedAt
                                 select new SupplementRow(s, u == null ? null : u.FirstName + " " + u.LastName)).ToListAsync(ct);
        var canEdit = await scope.CanEditAsync(opp.OwnerUserId) && await scope.CanSeeAsync(EntityTypes.Claim, id);
        return new ClaimDetail(claim, opp, contact, property, await carriers.GetByTenantCarrierAsync(claim.TenantCarrierId, ct),
            adjuster, desk, storm, supplements, canEdit);
    }

    public async Task<Guid?> ForOpportunityAsync(Guid opportunityId, CancellationToken ct = default) =>
        await (await scope.ClaimsAsync()).Where(c => c.OpportunityId == opportunityId).OrderBy(c => c.CreatedAt).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

    public async Task<Guid> CreateAsync(Guid opportunityId, ClaimInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ClaimsManage);
        var opp = await (await scope.OpportunitiesAsync()).FirstOrDefaultAsync(o => o.Id == opportunityId, ct)
                  ?? throw new RecordNotFoundException("Opportunity");
        await scope.EnsureCanEditAsync(opp.OwnerUserId);

        var claim = new InsuranceClaim
        {
            OpportunityId = opp.Id, PropertyId = opp.PropertyId,
            // Defaults from the lead's intake details; every field can still be blank.
            DateOfLoss = input.DateOfLoss ?? opp.DateOfLoss, PerilKey = input.PerilKey ?? opp.PerilKey,
            StormEventId = input.StormEventId ?? opp.StormEventId,
        };
        await ApplyAsync(claim, input, ct);
        db.Claims.Add(claim);
        opp.IsInsurance = true;
        await items.LogAsync(EntityTypes.Opportunity, opp.Id, "claim.created",
            claim.ClaimNumber is null ? "Insurance claim started (not filed yet)" : $"Claim {claim.ClaimNumber} added", ct);
        await db.SaveChangesAsync(ct);
        return claim.Id;
    }

    public async Task UpdateAsync(Guid id, ClaimInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ClaimsManage);
        var claim = await (await scope.ClaimsAsync()).FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new RecordNotFoundException("Claim");
        var opp = await db.Opportunities.FirstAsync(o => o.Id == claim.OpportunityId, ct);
        await scope.EnsureCanEditAsync(opp.OwnerUserId);

        var before = (claim.Status, claim.ClaimNumber);
        claim.DateOfLoss = input.DateOfLoss;
        claim.PerilKey = ContactService.Clean(input.PerilKey);
        claim.StormEventId = input.StormEventId;
        await ApplyAsync(claim, input, ct);

        if (before.Status != claim.Status)
            await items.LogAsync(EntityTypes.Claim, id, "claim.status_changed", $"Claim status: {Label(before.Status)} → {Label(claim.Status)}", ct);
        else if (before.ClaimNumber != claim.ClaimNumber && claim.ClaimNumber is not null)
            await items.LogAsync(EntityTypes.Claim, id, "claim.filed", $"Claim number {claim.ClaimNumber} recorded", ct);
        else
            await items.LogAsync(EntityTypes.Claim, id, "claim.updated", "Claim details updated", ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(InsuranceClaim c, ClaimInput i, CancellationToken ct)
    {
        if (!ClaimStatuses.All.Contains(i.Status)) throw new CrmRuleException("Unknown claim status.");
        if (i.Deductible is < 0 || i.ApprovedAmount is < 0) throw new CrmRuleException("Amounts can't be negative.");
        foreach (var contactId in new[] { i.AdjusterContactId, i.DeskAdjusterContactId }.OfType<Guid>())
            await scope.EnsureCanSeeAsync(EntityTypes.Contact, contactId);
        if (i.StormEventId is { } sid && !await db.StormEvents.AnyAsync(s => s.Id == sid, ct)) throw new CrmRuleException("Unknown storm.");

        c.TenantCarrierId = await carriers.ResolveChoiceAsync(i.CarrierChoice, ct);
        c.ClaimNumber = ContactService.Clean(i.ClaimNumber);
        c.PolicyNumber = ContactService.Clean(i.PolicyNumber);
        c.Deductible = i.Deductible;
        c.AdjusterContactId = i.AdjusterContactId;
        c.DeskAdjusterContactId = i.DeskAdjusterContactId;
        c.FiledOn = i.FiledOn;
        c.InspectionOn = i.InspectionOn;
        c.ApprovedAmount = i.ApprovedAmount;
        c.Notes = ContactService.Clean(i.Notes);
        // A claim number means it has been filed.
        c.Status = i.Status == ClaimStatuses.NotFiled && c.ClaimNumber is not null ? ClaimStatuses.Open : i.Status;
        if (c.Status != ClaimStatuses.NotFiled && c.FiledOn is null) c.FiledOn = DateOnly.FromDateTime(DateTime.UtcNow);
    }

    public async Task<Guid> SaveSupplementAsync(Guid claimId, Guid? supplementId, SupplementInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.ClaimsManage);
        var claim = await (await scope.ClaimsAsync()).FirstOrDefaultAsync(c => c.Id == claimId, ct) ?? throw new RecordNotFoundException("Claim");
        var opp = await db.Opportunities.FirstAsync(o => o.Id == claim.OpportunityId, ct);
        await scope.EnsureCanEditAsync(opp.OwnerUserId);
        if (string.IsNullOrWhiteSpace(input.Reason)) throw new CrmRuleException("Say what the supplement is for.");
        if (!SupplementStatuses.All.Contains(input.Status)) throw new CrmRuleException("Unknown supplement status.");
        if (input.RequestedAmount is < 0 || input.ApprovedAmount is < 0) throw new CrmRuleException("Amounts can't be negative.");

        Supplement s;
        string? oldStatus = null;
        if (supplementId is { } id)
        {
            s = await db.Supplements.FirstOrDefaultAsync(x => x.Id == id && x.ClaimId == claimId, ct) ?? throw new RecordNotFoundException("Supplement");
            oldStatus = s.Status;
        }
        else
        {
            s = new Supplement { ClaimId = claimId, SubmittedByUserId = scope.Me };
            db.Supplements.Add(s);
        }
        s.Reason = input.Reason.Trim();
        s.Status = input.Status;
        s.RequestedAmount = input.RequestedAmount;
        s.ApprovedAmount = input.ApprovedAmount;
        s.SentOn = input.SentOn ?? (input.Status != SupplementStatuses.Needed ? s.SentOn ?? DateOnly.FromDateTime(DateTime.UtcNow) : null);
        s.RespondedOn = input.RespondedOn ?? (input.Status is SupplementStatuses.Approved or SupplementStatuses.Partial or SupplementStatuses.Denied
            ? s.RespondedOn ?? DateOnly.FromDateTime(DateTime.UtcNow) : null);
        s.Notes = ContactService.Clean(input.Notes);

        await items.LogAsync(EntityTypes.Claim, claimId, supplementId is null ? "supplement.created" : "supplement.updated",
            supplementId is null ? $"Supplement added: {s.Reason}"
                : oldStatus != s.Status ? $"Supplement \"{s.Reason}\": {Label(oldStatus!)} → {Label(s.Status)}" : $"Supplement \"{s.Reason}\" updated", ct);
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    public static string Label(string status) => status switch
    {
        ClaimStatuses.NotFiled => "Not filed",
        _ => char.ToUpperInvariant(status[0]) + status[1..],
    };
}
