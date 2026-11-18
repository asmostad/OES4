using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Domain.Estimating;
using Oes.Domain.Insurance;
using Oes.Domain.Measurements;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Insurance;
using Oes.Infrastructure.Measurements;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Estimating;

public sealed class NewEstimateInput
{
    public Guid OpportunityId { get; set; }
    public string? Name { get; set; }
    public string Method { get; set; } = EstimateMethods.PriceBook;
    public Guid? PriceListId { get; set; }
}

public sealed class EstimateSettingsInput
{
    public string Name { get; set; } = "";
    public decimal OverheadPct { get; set; }
    public decimal ProfitPct { get; set; }
    public decimal TaxRate { get; set; }
    public string TaxBasis { get; set; } = TaxBases.Materials;
    public bool ShowLinePrices { get; set; } = true;
    public DateOnly? ExpiresOn { get; set; }
    public string? Terms { get; set; }
    public string? Notes { get; set; }
}

public sealed record EstimateRow(Guid Id, string Number, string Name, string Status, string Method, decimal Total, Guid OpportunityId,
    string ContactName, string? Address, string? OwnerName, int LatestVersionNo, int PendingApprovals, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SectionView(EstimateSection Section, List<EstimateLine> Lines, string? StructureName, string? TradeName, string? TradeColor,
    List<MeasurementOption> Measurements)
{
    public decimal Subtotal => Lines.Sum(l => l.Amount);
}

/// <summary>A measurement offered as a quantity: value converted to the unit a line usually wants.</summary>
public sealed record MeasurementOption(string Label, decimal Qty, string Unit);

public sealed record VersionRow(Guid Id, int VersionNo, string Status, decimal Total, DateTime IssuedAt, string? IssuedBy, string? RevisionReason, string? SignerName, DateTime? SignedAt);

public sealed record EstimateDetail(
    Estimate Estimate, Opportunity Opportunity, Contact Contact, Property? Property, InsuranceClaim? Claim, PriceList? PriceList,
    string? OwnerName, List<SectionView> Sections, List<VersionRow> Versions, bool CanEdit, bool CanApprove)
{
    public int PendingApprovals => Sections.Sum(s => s.Lines.Count(l => l.ApprovalStatus == ApprovalStatuses.Pending));
    public int RejectedLines => Sections.Sum(s => s.Lines.Count(l => l.ApprovalStatus == ApprovalStatuses.Rejected));
    public bool IsLocked => Estimate.Status == EstimateStatuses.Signed;
}

public sealed record ApprovalRow(Guid LineId, Guid EstimateId, string EstimateNumber, string EstimateName, string Description, decimal Qty, string Unit,
    decimal UnitPrice, decimal? DefaultUnitPrice, string? Reason, string? RequestedBy, Guid? RequestedById, DateTime RequestedAt);

/// <summary>Frozen content of an issued version: everything needed to reprint or audit it.</summary>
public sealed record VersionContent(
    string Number, string Name, string Method, string? PriceListName, string CustomerName, string? CustomerEmail, string? CustomerPhone,
    string? PropertyAddress, string? ClaimNumber, string? CarrierName, List<VersionSection> Sections,
    decimal Subtotal, decimal OverheadPct, decimal Overhead, decimal ProfitPct, decimal Profit, decimal TaxRate, decimal Tax, decimal Total,
    bool ShowLinePrices, string? Terms, DateOnly? ExpiresOn, DateTime IssuedAt, int VersionNo);

public sealed record VersionSection(string Name, string? ScopeOfWork, string? MeasurementSummary, List<VersionLine> Lines, decimal Subtotal);

public sealed record VersionLine(string Kind, string? Code, string Activity, string Description, decimal Qty, string Unit, decimal UnitPrice,
    decimal Amount, decimal? DefaultUnitPrice, Guid? PriceListItemId, Guid? ProductId, string? Notes);

/// <summary>
/// Estimates with two methods (price book and line item) on one line model (decision 4).
/// Prices are copied onto lines; exceptions need approval by someone else; issuing freezes a version.
/// </summary>
public sealed class EstimateService(
    OesDbContext db, RecordScope scope, RecordItems items, INumberSequence numbers, IClock clock, ICurrentUser user,
    OpportunityService opportunities, CarrierService carriers, Work.NotificationService notifications)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------------- Reading ----------------

    public async Task<List<EstimateRow>> ListAsync(string? q, string? status, bool mine, Guid? opportunityId = null, CancellationToken ct = default)
    {
        var ests = await scope.EstimatesAsync();
        if (opportunityId is { } oid) ests = ests.Where(e => e.OpportunityId == oid);
        if (!string.IsNullOrWhiteSpace(status)) ests = ests.Where(e => e.Status == status);
        if (mine) ests = ests.Where(e => e.OwnerUserId == scope.Me);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            ests = ests.Where(e => e.Number.Contains(t) || e.Name.Contains(t)
                || db.Opportunities.Any(o => o.Id == e.OpportunityId && db.Contacts.Any(c => c.Id == o.PrimaryContactId && c.DisplayName.Contains(t))));
        }
        var rows = await (from e in ests
                          join o in db.Opportunities on e.OpportunityId equals o.Id
                          join c in db.Contacts on o.PrimaryContactId equals c.Id
                          join p in db.Properties on e.PropertyId equals p.Id into ps from p in ps.DefaultIfEmpty()
                          join u in db.Users on e.OwnerUserId equals u.Id into us from u in us.DefaultIfEmpty()
                          orderby e.UpdatedAt descending
                          select new
                          {
                              e, c.DisplayName, p, Owner = u == null ? null : u.FirstName + " " + u.LastName,
                              Pending = db.EstimateLines.Count(l => l.EstimateId == e.Id && l.ApprovalStatus == ApprovalStatuses.Pending),
                          }).Take(300).ToListAsync(ct);
        return rows.Select(r => new EstimateRow(r.e.Id, r.e.Number, r.e.Name, r.e.Status, r.e.Method, r.e.Total, r.e.OpportunityId, r.DisplayName,
            r.p?.SingleLine, r.Owner, r.e.LatestVersionNo, r.Pending, r.e.CreatedAt, r.e.UpdatedAt)).ToList();
    }

    public async Task<EstimateDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var est = await (await scope.EstimatesAsync(includeArchived: true)).FirstOrDefaultAsync(e => e.Id == id, ct)
                  ?? throw new RecordNotFoundException("Estimate");
        var opp = await db.Opportunities.FirstAsync(o => o.Id == est.OpportunityId, ct);
        var contact = await db.Contacts.Include(c => c.Methods).FirstAsync(c => c.Id == opp.PrimaryContactId, ct);
        var property = est.PropertyId is null ? null : await db.Properties.FirstOrDefaultAsync(p => p.Id == est.PropertyId, ct);
        var claim = est.ClaimId is null ? null : await db.Claims.FirstOrDefaultAsync(c => c.Id == est.ClaimId, ct);
        var priceList = est.PriceListId is null ? null : await db.PriceLists.FirstOrDefaultAsync(p => p.Id == est.PriceListId, ct);
        var owner = est.OwnerUserId is null ? null
            : await db.Users.Where(u => u.Id == est.OwnerUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);

        var sections = await db.EstimateSections.Where(s => s.EstimateId == id).OrderBy(s => s.Sort).ToListAsync(ct);
        var lines = await db.EstimateLines.Where(l => l.EstimateId == id).OrderBy(l => l.Sort).ToListAsync(ct);
        var structureIds = sections.Select(s => s.StructureId).OfType<Guid>().ToList();
        var structures = await db.PropertyStructures.Where(s => structureIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var trades = await db.TradeTypes.ToDictionaryAsync(t => t.Id, ct);
        var setIds = sections.Select(s => s.MeasurementSetId).OfType<Guid>().ToList();
        var sets = await db.MeasurementSets.Include(s => s.Values).Where(s => setIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var fieldDefs = await db.MeasurementFieldDefs.ToListAsync(ct);

        var views = sections.Select(s =>
        {
            var trade = s.TradeTypeId is { } tid ? trades.GetValueOrDefault(tid) : null;
            var set = s.MeasurementSetId is { } sid ? sets.GetValueOrDefault(sid) : null;
            return new SectionView(s, lines.Where(l => l.SectionId == s.Id).ToList(),
                s.StructureId is { } st ? structures.GetValueOrDefault(st)?.Name : null, trade?.Name, trade?.Color,
                set is null ? [] : MeasurementOptions(set, fieldDefs));
        }).ToList();

        var versions = await (from v in db.EstimateVersions where v.EstimateId == id
                              join u in db.Users on v.IssuedByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                              orderby v.VersionNo descending
                              select new VersionRow(v.Id, v.VersionNo, v.Status, v.Total, v.CreatedAt, u == null ? null : u.FirstName + " " + u.LastName,
                                  v.RevisionReason, v.SignerName, v.SignedAt)).ToListAsync(ct);

        return new EstimateDetail(est, opp, contact, property, claim, priceList, owner, views, versions,
            await CanEditAsync(est), await user.HasPermissionAsync(Permissions.PricingApprove, ct));
    }

    /// <summary>Measurement values offered as quantities, in the units estimate lines usually use (SQ for roof area).</summary>
    public static List<MeasurementOption> MeasurementOptions(MeasurementSet set, List<MeasurementFieldDef> defs)
    {
        var result = new List<MeasurementOption>();
        foreach (var g in set.Values.GroupBy(v => v.FieldKey))
        {
            var def = defs.FirstOrDefault(d => d.TradeTypeId == set.TradeTypeId && d.Key == g.Key);
            var label = def?.Label ?? g.Key;
            var total = g.Sum(v => v.Value);
            var unit = def?.Unit ?? "";
            if (unit == "sq ft" && g.Key.Contains("area") && g.Key != "openings_area")
            {
                result.Add(new MeasurementOption($"{label} (squares)", Math.Round(total / 100m, 2), "SQ"));
                result.Add(new MeasurementOption(label, total, "SF"));
            }
            else if (unit is "LF" or "ea" or "UI" or "sq ft")
            {
                result.Add(new MeasurementOption(label, total, unit switch { "ea" => "EA", "sq ft" => "SF", _ => unit }));
            }
        }
        return result;
    }

    private async Task<bool> CanEditAsync(Estimate est) =>
        est.Status != EstimateStatuses.Signed && await HasEditRightsAsync(est);

    /// <summary>Whether this user may work on the estimate, whatever its status (signed estimates still take supplements).</summary>
    public async Task<bool> HasEditRightsAsync(Estimate est)
    {
        if (est.ArchivedAt is not null) return false;
        if (await user.HasPermissionAsync(Permissions.EstimatesManage)) return true;
        return await user.HasPermissionAsync(Permissions.EstimatesCreate) && (est.OwnerUserId is null || est.OwnerUserId == scope.Me);
    }

    private async Task<Estimate> EditableAsync(Guid id, CancellationToken ct)
    {
        var est = await (await scope.EstimatesAsync()).FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new RecordNotFoundException("Estimate");
        if (est.Status == EstimateStatuses.Signed) throw new CrmRuleException("This estimate is signed. Create a change order instead (coming with projects).");
        if (!await CanEditAsync(est)) throw new RecordForbiddenException("You can't edit this estimate.");
        // Editing an issued estimate starts the next revision; the issued version stays frozen.
        if (est.Status is EstimateStatuses.Issued or EstimateStatuses.Declined) est.Status = EstimateStatuses.Draft;
        return est;
    }

    // ---------------- Create and settings ----------------

    public async Task<Guid> CreateAsync(NewEstimateInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.EstimatesCreate);
        var opp = await (await scope.OpportunitiesAsync()).FirstOrDefaultAsync(o => o.Id == input.OpportunityId, ct)
                  ?? throw new RecordNotFoundException("Opportunity");
        if (input.Method is not (EstimateMethods.PriceBook or EstimateMethods.LineItem)) throw new CrmRuleException("Unknown estimate method.");
        PriceList? list = null;
        if (input.Method == EstimateMethods.PriceBook)
        {
            if (input.PriceListId is not { } plId) throw new CrmRuleException("Pick a price list for a price-book estimate.");
            list = await db.PriceLists.FirstOrDefaultAsync(p => p.Id == plId && p.Status == PriceListStatuses.Active, ct)
                   ?? throw new CrmRuleException("Pick an active price list.");
        }

        var claim = await db.Claims.Where(c => c.OpportunityId == opp.Id && c.ArchivedAt == null).OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        var est = new Estimate
        {
            Number = await numbers.NextAsync(NumberedKinds.Estimate, ct),
            Name = ContactService.Clean(input.Name) ?? opp.Name,
            OpportunityId = opp.Id, PropertyId = opp.PropertyId, ClaimId = claim?.Id, OwnerUserId = scope.Me ?? opp.OwnerUserId,
            Method = input.Method, PriceListId = list?.Id,
            ExpiresOn = DateOnly.FromDateTime(clock.UtcNow.AddDays(30)),
            // Insurance work usually carries 10% overhead and 10% profit; retail usually doesn't show O&P.
            OverheadPct = opp.IsInsurance ? 10m : 0m, ProfitPct = opp.IsInsurance ? 10m : 0m,
        };
        db.Estimates.Add(est);

        // One section per current measurement set on the property, so quantities are a click away.
        var sort = 0;
        if (opp.PropertyId is { } pid)
        {
            var current = await (from s in db.MeasurementSets where s.PropertyId == pid && s.IsCurrent
                                 join st in db.PropertyStructures on s.StructureId equals st.Id
                                 join t in db.TradeTypes on s.TradeTypeId equals t.Id
                                 orderby st.Sort, t.Sort
                                 select new { s.Id, s.StructureId, s.TradeTypeId, Name = st.Name + " – " + t.Name }).ToListAsync(ct);
            foreach (var c in current)
                db.EstimateSections.Add(new EstimateSection { EstimateId = est.Id, Name = c.Name, StructureId = c.StructureId, TradeTypeId = c.TradeTypeId, MeasurementSetId = c.Id, Sort = sort += 10 });
        }
        if (sort == 0) db.EstimateSections.Add(new EstimateSection { EstimateId = est.Id, Name = "Scope of work", Sort = 10 });

        await items.LogAsync(EntityTypes.Opportunity, opp.Id, "estimate.created", $"Estimate {est.Number} started ({(est.Method == EstimateMethods.PriceBook ? "price book: " + list!.Name : "line items")})", ct);
        await db.SaveChangesAsync(ct);
        return est.Id;
    }

    public async Task UpdateSettingsAsync(Guid id, EstimateSettingsInput i, CancellationToken ct = default)
    {
        var est = await EditableAsync(id, ct);
        if (string.IsNullOrWhiteSpace(i.Name)) throw new CrmRuleException("Estimate name is required.");
        if (i.OverheadPct is < 0 or > 100 || i.ProfitPct is < 0 or > 100 || i.TaxRate is < 0 or > 30) throw new CrmRuleException("Check the percentages.");
        if (i.TaxBasis is not (TaxBases.Materials or TaxBases.All or TaxBases.None)) throw new CrmRuleException("Unknown tax basis.");
        est.Name = i.Name.Trim();
        est.OverheadPct = i.OverheadPct;
        est.ProfitPct = i.ProfitPct;
        est.TaxRate = i.TaxRate;
        est.TaxBasis = i.TaxBasis;
        est.ShowLinePrices = i.ShowLinePrices;
        est.ExpiresOn = i.ExpiresOn;
        est.Terms = ContactService.Clean(i.Terms);
        est.Notes = ContactService.Clean(i.Notes);
        await RecalcAsync(est, ct);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Sections ----------------

    public async Task AddSectionAsync(Guid id, string name, Guid? structureId, Guid? tradeTypeId, CancellationToken ct = default)
    {
        var est = await EditableAsync(id, ct);
        if (string.IsNullOrWhiteSpace(name)) throw new CrmRuleException("Section name is required.");
        Guid? setId = null;
        if (structureId is { } sid)
        {
            if (!await db.PropertyStructures.AnyAsync(s => s.Id == sid && s.PropertyId == est.PropertyId, ct)) throw new CrmRuleException("That structure isn't on this property.");
            if (tradeTypeId is { } tid)
                setId = await db.MeasurementSets.Where(m => m.StructureId == sid && m.TradeTypeId == tid && m.IsCurrent).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
        }
        var sort = (await db.EstimateSections.Where(s => s.EstimateId == id).MaxAsync(s => (int?)s.Sort, ct) ?? 0) + 10;
        db.EstimateSections.Add(new EstimateSection { EstimateId = id, Name = name.Trim(), StructureId = structureId, TradeTypeId = tradeTypeId, MeasurementSetId = setId, Sort = sort });
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateSectionAsync(Guid sectionId, string name, string? scopeOfWork, CancellationToken ct = default)
    {
        var section = await db.EstimateSections.FirstOrDefaultAsync(s => s.Id == sectionId, ct) ?? throw new RecordNotFoundException("Section");
        await EditableAsync(section.EstimateId, ct);
        if (string.IsNullOrWhiteSpace(name)) throw new CrmRuleException("Section name is required.");
        section.Name = name.Trim();
        section.ScopeOfWork = ContactService.Clean(scopeOfWork);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSectionAsync(Guid sectionId, CancellationToken ct = default)
    {
        var section = await db.EstimateSections.FirstOrDefaultAsync(s => s.Id == sectionId, ct) ?? throw new RecordNotFoundException("Section");
        var est = await EditableAsync(section.EstimateId, ct);
        if (await db.EstimateSections.CountAsync(s => s.EstimateId == est.Id, ct) == 1) throw new CrmRuleException("An estimate needs at least one section.");
        db.EstimateLines.RemoveRange(db.EstimateLines.Where(l => l.SectionId == sectionId));
        db.EstimateSections.Remove(section);
        await db.SaveChangesAsync(ct);
        await RecalcAsync(est, ct);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Lines ----------------

    public async Task<Guid> AddPriceBookLineAsync(Guid id, Guid sectionId, Guid priceListItemId, decimal qty, CancellationToken ct = default)
    {
        var est = await EditableAsync(id, ct);
        if (est.Method != EstimateMethods.PriceBook || est.PriceListId is null) throw new CrmRuleException("Price-book lines need a price-book estimate.");
        await EnsureSectionAsync(id, sectionId, ct);
        var row = await (from p in db.PriceListItems
                         join i in db.CatalogItems on p.CatalogItemId equals i.Id
                         join c in db.CatalogCategories on i.CategoryId equals c.Id
                         where p.Id == priceListItemId && p.PriceListId == est.PriceListId
                         select new { p, i, c }).FirstOrDefaultAsync(ct)
                  ?? throw new CrmRuleException("That item isn't on this estimate's price list.");

        // Price, code and description are copied now; later price list edits never touch this line.
        var line = NewLine(est, sectionId, LineKinds.PriceBook, qty);
        line.CatalogItemId = row.i.Id;
        line.PriceListItemId = row.p.Id;
        line.Activity = row.p.Activity;
        line.Code = $"{row.c.Code} {row.i.Code}";
        line.Description = row.i.Description;
        line.Unit = row.i.Unit;
        line.UnitPrice = row.p.UnitPrice;
        line.DefaultUnitPrice = row.p.UnitPrice;
        line.IsMaterial = (row.p.MaterialPrice ?? 0) > 0;
        return await SaveNewLineAsync(est, line, ct);
    }

    public async Task<Guid> AddProductLineAsync(Guid id, Guid sectionId, Guid productId, decimal qty, CancellationToken ct = default)
    {
        var est = await EditableAsync(id, ct);
        await EnsureSectionAsync(id, sectionId, ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.IsActive, ct) ?? throw new CrmRuleException("Unknown product.");
        var line = NewLine(est, sectionId, LineKinds.Product, qty);
        line.ProductId = product.Id;
        line.Code = product.Sku;
        line.Description = product.Name;
        line.Unit = product.Unit;
        line.UnitPrice = product.DefaultPrice;
        line.DefaultUnitPrice = product.DefaultPrice;
        line.UnitCost = product.DefaultCost;
        line.IsMaterial = product.IsMaterial;
        return await SaveNewLineAsync(est, line, ct);
    }

    /// <summary>A custom line (no catalog item or product). Always needs approval.</summary>
    public async Task<Guid> AddMiscLineAsync(Guid id, Guid sectionId, string description, decimal qty, string unit, decimal unitPrice, bool isMaterial, CancellationToken ct = default)
    {
        var est = await EditableAsync(id, ct);
        await EnsureSectionAsync(id, sectionId, ct);
        if (string.IsNullOrWhiteSpace(description)) throw new CrmRuleException("Describe the custom line.");
        if (unitPrice < 0) throw new CrmRuleException("Prices can't be negative.");
        var line = NewLine(est, sectionId, LineKinds.Misc, qty);
        line.Description = description.Trim();
        line.Unit = string.IsNullOrWhiteSpace(unit) ? "EA" : unit.Trim().ToUpperInvariant();
        line.UnitPrice = unitPrice;
        line.IsMaterial = isMaterial;
        return await SaveNewLineAsync(est, line, ct);
    }

    public async Task UpdateLineAsync(Guid lineId, decimal qty, decimal unitPrice, string? description, string? notes, CancellationToken ct = default)
    {
        var line = await db.EstimateLines.FirstOrDefaultAsync(l => l.Id == lineId, ct) ?? throw new RecordNotFoundException("Line");
        var est = await EditableAsync(line.EstimateId, ct);
        if (qty < 0) throw new CrmRuleException("Quantity can't be negative.");
        if (unitPrice < 0) throw new CrmRuleException("Prices can't be negative.");
        var priceChanged = unitPrice != line.UnitPrice;
        line.Qty = qty;
        line.UnitPrice = unitPrice;
        if (line.Kind == LineKinds.Misc && !string.IsNullOrWhiteSpace(description)) line.Description = description.Trim();
        line.Notes = ContactService.Clean(notes);
        if (priceChanged || line.ApprovalStatus == ApprovalStatuses.Rejected)
        {
            EvaluateApproval(line);
            await NotifyApproversAsync(est, line, ct);
        }
        await RecalcAsync(est, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteLineAsync(Guid lineId, CancellationToken ct = default)
    {
        var line = await db.EstimateLines.FirstOrDefaultAsync(l => l.Id == lineId, ct) ?? throw new RecordNotFoundException("Line");
        var est = await EditableAsync(line.EstimateId, ct);
        db.EstimateLines.Remove(line);
        await db.SaveChangesAsync(ct);
        await RecalcAsync(est, ct);
        await db.SaveChangesAsync(ct);
    }

    private EstimateLine NewLine(Estimate est, Guid sectionId, string kind, decimal qty)
    {
        if (qty < 0) throw new CrmRuleException("Quantity can't be negative.");
        return new EstimateLine { EstimateId = est.Id, SectionId = sectionId, Kind = kind, Qty = qty };
    }

    private async Task<Guid> SaveNewLineAsync(Estimate est, EstimateLine line, CancellationToken ct)
    {
        line.Sort = (await db.EstimateLines.Where(l => l.SectionId == line.SectionId).MaxAsync(l => (int?)l.Sort, ct) ?? 0) + 10;
        EvaluateApproval(line);
        db.EstimateLines.Add(line);
        await RecalcAsync(est, ct, extra: line);
        await NotifyApproversAsync(est, line, ct);
        await db.SaveChangesAsync(ct);
        return line.Id;
    }

    private async Task NotifyApproversAsync(Estimate est, EstimateLine line, CancellationToken ct)
    {
        if (line.ApprovalStatus != ApprovalStatuses.Pending) return;
        await notifications.NotifyPermissionAsync(Permissions.PricingApprove, "pricing.approval_needed",
            $"Price approval needed on {est.Number}", $"{line.Description} · {line.ApprovalReason}", "/Estimates/Approvals", ct);
    }

    private async Task EnsureSectionAsync(Guid estimateId, Guid sectionId, CancellationToken ct)
    {
        if (!await db.EstimateSections.AnyAsync(s => s.Id == sectionId && s.EstimateId == estimateId, ct))
            throw new CrmRuleException("That section isn't on this estimate.");
    }

    /// <summary>Approval by exception: custom lines and prices below the list/default price.</summary>
    private void EvaluateApproval(EstimateLine line)
    {
        string? reason = null;
        if (line.Kind == LineKinds.Misc) reason = "Custom line";
        else if (line.DefaultUnitPrice is { } d && line.UnitPrice < d) reason = $"Below list price ({d:C2})";
        else if (line.DefaultUnitPrice is null or 0 && line.UnitPrice == 0) reason = "No price";

        if (reason is null)
        {
            line.ApprovalStatus = ApprovalStatuses.None;
            line.ApprovalReason = null;
            return;
        }
        line.ApprovalStatus = ApprovalStatuses.Pending;
        line.ApprovalReason = reason;
        line.ApprovalRequestedBy = scope.Me;
        line.ApprovalDecidedBy = null;
        line.ApprovalDecidedAt = null;
        line.ApprovalNote = null;
    }

    // ---------------- Approvals ----------------

    public async Task<List<ApprovalRow>> PendingApprovalsAsync(CancellationToken ct = default)
    {
        var ests = await scope.EstimatesAsync();
        return await (from l in db.EstimateLines where l.ApprovalStatus == ApprovalStatuses.Pending
                      join e in ests on l.EstimateId equals e.Id
                      join u in db.Users on l.ApprovalRequestedBy equals u.Id into us from u in us.DefaultIfEmpty()
                      orderby l.UpdatedAt
                      select new ApprovalRow(l.Id, e.Id, e.Number, e.Name, l.Description, l.Qty, l.Unit, l.UnitPrice, l.DefaultUnitPrice, l.ApprovalReason,
                          u == null ? null : u.FirstName + " " + u.LastName, l.ApprovalRequestedBy, l.UpdatedAt)).ToListAsync(ct);
    }

    public async Task DecideAsync(Guid lineId, bool approve, string? note, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.PricingApprove);
        var line = await db.EstimateLines.FirstOrDefaultAsync(l => l.Id == lineId, ct) ?? throw new RecordNotFoundException("Line");
        await scope.EnsureCanSeeAsync(EntityTypes.Estimate, line.EstimateId);
        if (line.ApprovalStatus != ApprovalStatuses.Pending) throw new CrmRuleException("That line isn't waiting for approval.");
        if (line.ApprovalRequestedBy == scope.Me) throw new RecordForbiddenException("You can't approve your own price exception. Ask another approver.");
        if (!approve && string.IsNullOrWhiteSpace(note)) throw new CrmRuleException("Say why the price was rejected.");
        line.ApprovalStatus = approve ? ApprovalStatuses.Approved : ApprovalStatuses.Rejected;
        line.ApprovalDecidedBy = scope.Me;
        line.ApprovalDecidedAt = clock.UtcNow;
        line.ApprovalNote = ContactService.Clean(note);
        var est = await db.Estimates.FirstAsync(e => e.Id == line.EstimateId, ct);
        await items.LogAsync(EntityTypes.Estimate, est.Id, approve ? "pricing.approved" : "pricing.rejected",
            $"{(approve ? "Approved" : "Rejected")} price on \"{line.Description}\"{(note is null ? "" : ": " + note.Trim())}", ct);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Totals ----------------

    private async Task RecalcAsync(Estimate est, CancellationToken ct, EstimateLine? extra = null)
    {
        var lines = await db.EstimateLines.Where(l => l.EstimateId == est.Id).ToListAsync(ct);
        // Include tracked, unsaved changes (new line not yet in the database, edited quantities).
        var tracked = db.ChangeTracker.Entries<EstimateLine>().Where(e => e.Entity.EstimateId == est.Id).ToList();
        var all = lines.Where(l => tracked.All(t => t.Entity.Id != l.Id || t.State != EntityState.Deleted))
            .Select(l => tracked.FirstOrDefault(t => t.Entity.Id == l.Id)?.Entity ?? l).ToList();
        if (extra is not null && all.All(l => l.Id != extra.Id)) all.Add(extra);
        var t = Totals(all, est.OverheadPct, est.ProfitPct, est.TaxRate, est.TaxBasis);
        (est.Subtotal, est.Overhead, est.Profit, est.Tax, est.Total) = t;
    }

    /// <summary>Xactimate-style: O&P on the line subtotal; tax on materials (or everything) before O&P.</summary>
    public static (decimal Subtotal, decimal Overhead, decimal Profit, decimal Tax, decimal Total) Totals(
        IEnumerable<EstimateLine> lines, decimal overheadPct, decimal profitPct, decimal taxRate, string taxBasis)
    {
        var list = lines.ToList();
        var subtotal = list.Sum(l => l.Amount);
        var taxable = taxBasis switch
        {
            TaxBases.All => list.Where(l => l.IsTaxable).Sum(l => l.Amount),
            TaxBases.Materials => list.Where(l => l.IsTaxable && l.IsMaterial).Sum(l => l.Amount),
            _ => 0m,
        };
        var r = (decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        var overhead = r(subtotal * overheadPct / 100m);
        var profit = r(subtotal * profitPct / 100m);
        var tax = r(taxable * taxRate / 100m);
        return (subtotal, overhead, profit, tax, subtotal + overhead + profit + tax);
    }

    // ---------------- Versions ----------------

    /// <summary>Freezes the estimate as a new version. Blocked while price exceptions are waiting or rejected.</summary>
    public async Task<Guid> IssueAsync(Guid id, string? revisionReason, CancellationToken ct = default)
    {
        var est = await EditableAsync(id, ct);
        var detail = await GetAsync(id, ct);
        if (detail.Sections.Sum(s => s.Lines.Count) == 0) throw new CrmRuleException("Add at least one line before issuing.");
        if (detail.PendingApprovals > 0) throw new CrmRuleException($"{detail.PendingApprovals} price exception(s) are waiting for approval.");
        if (detail.RejectedLines > 0) throw new CrmRuleException("Fix or remove the rejected lines before issuing.");
        if (est.LatestVersionNo > 0 && string.IsNullOrWhiteSpace(revisionReason)) throw new CrmRuleException("Say what changed in this revision.");

        await RecalcAsync(est, ct);
        var content = await SnapshotAsync(detail, est, est.LatestVersionNo + 1, ct);
        var json = JsonSerializer.Serialize(content, Json);
        foreach (var old in await db.EstimateVersions.Where(v => v.EstimateId == id && v.Status == VersionStatuses.Issued).ToListAsync(ct))
            old.Status = VersionStatuses.Superseded;

        var version = new EstimateVersion
        {
            EstimateId = id, VersionNo = est.LatestVersionNo + 1, IssuedByUserId = scope.Me, RevisionReason = ContactService.Clean(revisionReason),
            Subtotal = est.Subtotal, Overhead = est.Overhead, Profit = est.Profit, Tax = est.Tax, Total = est.Total,
            ContentJson = json, ContentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
        };
        db.EstimateVersions.Add(version);
        est.LatestVersionNo = version.VersionNo;
        est.Status = EstimateStatuses.Issued;
        await items.LogAsync(EntityTypes.Estimate, id, "estimate.issued", $"Version {version.VersionNo} issued · {est.Total:C2}", ct);
        await db.SaveChangesAsync(ct);
        return version.Id;
    }

    private async Task<VersionContent> SnapshotAsync(EstimateDetail d, Estimate est, int versionNo, CancellationToken ct)
    {
        var carrier = d.Claim?.TenantCarrierId is { } tc ? (await carriers.GetByTenantCarrierAsync(tc, ct))?.Name : null;
        string? Method(string kind) => d.Contact.Methods.Where(m => kind == ContactMethodKinds.Email ? m.Kind == kind : ContactMethodKinds.IsPhone(m.Kind))
            .OrderByDescending(m => m.IsPrimary).Select(m => m.Value).FirstOrDefault();
        var sections = d.Sections.Where(s => s.Lines.Count > 0).Select(s => new VersionSection(
            s.Section.Name, s.Section.ScopeOfWork,
            s.Measurements.Count == 0 ? null : string.Join(" · ", s.Measurements.Where(m => m.Unit is "SQ" or "LF").Take(6).Select(m => $"{m.Label}: {m.Qty:0.##} {m.Unit}")),
            s.Lines.Select(l => new VersionLine(l.Kind, l.Code, l.Activity, l.Description, l.Qty, l.Unit, l.UnitPrice, l.Amount, l.DefaultUnitPrice,
                l.PriceListItemId, l.ProductId, l.Notes)).ToList(),
            s.Subtotal)).ToList();
        return new VersionContent(est.Number, est.Name, est.Method, d.PriceList?.Name, d.Contact.DisplayName, Method(ContactMethodKinds.Email),
            Method(ContactMethodKinds.Mobile), d.Property?.SingleLine, d.Claim?.ClaimNumber, carrier, sections,
            est.Subtotal, est.OverheadPct, est.Overhead, est.ProfitPct, est.Profit, est.TaxRate, est.Tax, est.Total,
            est.ShowLinePrices, est.Terms, est.ExpiresOn, clock.UtcNow, versionNo);
    }

    public async Task<(EstimateVersion Version, VersionContent Content, Estimate Estimate)> GetVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var v = await db.EstimateVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct) ?? throw new RecordNotFoundException("Version");
        var est = await (await scope.EstimatesAsync(true)).FirstOrDefaultAsync(e => e.Id == v.EstimateId, ct) ?? throw new RecordNotFoundException("Version");
        return (v, JsonSerializer.Deserialize<VersionContent>(v.ContentJson, Json)!, est);
    }

    /// <summary>Records the customer's signature on the latest issued version and marks the opportunity won.</summary>
    public async Task MarkSignedAsync(Guid versionId, string signerName, CancellationToken ct = default)
    {
        var v = await db.EstimateVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct) ?? throw new RecordNotFoundException("Version");
        var est = await (await scope.EstimatesAsync()).FirstOrDefaultAsync(e => e.Id == v.EstimateId, ct) ?? throw new RecordNotFoundException("Version");
        if (!await CanEditAsync(est)) throw new RecordForbiddenException("You can't change this estimate.");
        if (v.Status != VersionStatuses.Issued || v.VersionNo != est.LatestVersionNo || est.Status != EstimateStatuses.Issued)
            throw new CrmRuleException("Only the latest issued version can be signed.");
        if (string.IsNullOrWhiteSpace(signerName)) throw new CrmRuleException("Enter who signed.");

        v.Status = VersionStatuses.Signed;
        v.SignedAt = clock.UtcNow;
        v.SignerName = signerName.Trim();
        est.Status = EstimateStatuses.Signed;
        est.SignedVersionId = v.Id;
        await items.LogAsync(EntityTypes.Estimate, est.Id, "estimate.signed", $"Version {v.VersionNo} signed by {v.SignerName} · {v.Total:C2}", ct);
        await db.SaveChangesAsync(ct);

        var opp = await db.Opportunities.FirstAsync(o => o.Id == est.OpportunityId, ct);
        if (opp.Outcome == Outcomes.Open)
        {
            var won = await db.PipelineStages.FirstOrDefaultAsync(s => s.Outcome == Outcomes.Won && s.IsActive, ct);
            if (won is not null)
            {
                if (opp.ExpectedValue is null) { opp.ExpectedValue = v.Total; await db.SaveChangesAsync(ct); }
                await opportunities.MoveStageAsync(opp.Id, won.Id, $"Estimate {est.Number} v{v.VersionNo} signed", null, ct);
            }
        }
    }

    public async Task MarkDeclinedAsync(Guid versionId, string? reason, CancellationToken ct = default)
    {
        var v = await db.EstimateVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct) ?? throw new RecordNotFoundException("Version");
        var est = await (await scope.EstimatesAsync()).FirstOrDefaultAsync(e => e.Id == v.EstimateId, ct) ?? throw new RecordNotFoundException("Version");
        if (!await CanEditAsync(est)) throw new RecordForbiddenException("You can't change this estimate.");
        if (v.Status != VersionStatuses.Issued) throw new CrmRuleException("Only an issued version can be declined.");
        v.Status = VersionStatuses.Declined;
        est.Status = EstimateStatuses.Declined;
        await items.LogAsync(EntityTypes.Estimate, est.Id, "estimate.declined", $"Version {v.VersionNo} declined{(string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason.Trim())}", ct);
        await db.SaveChangesAsync(ct);
    }
}
