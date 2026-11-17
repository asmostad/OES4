using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Common;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Crm;

public sealed class OpportunityInput
{
    public string? Name { get; set; }
    public Guid PrimaryContactId { get; set; }
    public Guid? PropertyId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? SourceKey { get; set; }
    public Guid? StageId { get; set; }
    public decimal? ExpectedValue { get; set; }
    public bool IsInsurance { get; set; }
    public string? PerilKey { get; set; }
    public DateOnly? DateOfLoss { get; set; }
    public Guid? StormEventId { get; set; }
    public string? Description { get; set; }
}

public sealed record OpportunityRow(
    Guid Id, string Number, string Name, Guid StageId, string StageName, string StageColor, string Outcome,
    Guid ContactId, string ContactName, Guid? PropertyId, string? Address, string? OwnerName, decimal? ExpectedValue,
    bool IsInsurance, DateTime CreatedAt, DateTime? StageChangedAt, bool IsStale);

public sealed record OpportunityDetail(
    Opportunity Opportunity, PipelineStage Stage, Contact Contact, Property? Property, string? OwnerName,
    List<PipelineStage> Stages, List<StageChangeRow> History, bool CanEdit);

public sealed record StageChangeRow(DateTime At, string? From, string To, string? By, string? Note);

public sealed record BoardColumn(PipelineStage Stage, List<OpportunityRow> Cards);

public sealed class OpportunityService(
    OesDbContext db, RecordScope scope, RecordItems items, INumberSequence numbers, IClock clock,
    ContactService contacts, PropertyService properties)
{
    public sealed record DashboardCounts(int Open, decimal OpenValue, int NeedAddress, int Stale, int WonThisMonth);

    /// <summary>Headline pipeline numbers for the dashboard, over what the user can see.</summary>
    public async Task<DashboardCounts> DashboardAsync(CancellationToken ct = default)
    {
        var opps = await scope.OpportunitiesAsync();
        var open = opps.Where(o => o.Outcome == Outcomes.Open);
        var now = clock.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var staleRows = await (from o in open
                               join s in db.PipelineStages on o.StageId equals s.Id
                               where s.StaleAfterHours != null
                               select new { Changed = o.StageChangedAt ?? o.CreatedAt, Hours = s.StaleAfterHours!.Value }).ToListAsync(ct);
        return new DashboardCounts(
            await open.CountAsync(ct),
            await open.SumAsync(o => o.ExpectedValue ?? 0, ct),
            await open.CountAsync(o => o.PropertyId == null, ct),
            staleRows.Count(r => (now - r.Changed).TotalHours > r.Hours),
            await opps.CountAsync(o => o.Outcome == Outcomes.Won && o.WonAt >= monthStart, ct));
    }

    public async Task<List<PipelineStage>> StagesAsync(CancellationToken ct = default) =>
        await db.PipelineStages.Where(s => s.IsActive).OrderBy(s => s.Sort).ToListAsync(ct);

    public async Task<PagedResult<OpportunityRow>> SearchAsync(string? q, Guid? stageId, string? outcome, bool mine, int page = 1,
        int pageSize = 25, CancellationToken ct = default, Guid? stormId = null)
    {
        var opps = await Filter(q, stageId, outcome, mine, stormId);
        var total = await opps.CountAsync(ct);
        page = Math.Max(1, page);
        var rows = await Rows(opps.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize), ct);
        return new PagedResult<OpportunityRow>(rows, total, page, pageSize);
    }

    public async Task<List<BoardColumn>> BoardAsync(string? q, bool mine, CancellationToken ct = default, Guid? stormId = null)
    {
        var stages = await StagesAsync(ct);
        var opps = await Filter(q, null, null, mine, stormId);
        // Open work, plus anything closed in the last 30 days so wins and losses stay visible for a while.
        var cutoff = clock.UtcNow.AddDays(-30);
        opps = opps.Where(o => o.Outcome == Outcomes.Open || o.ClosedAt >= cutoff);
        var rows = await Rows(opps.OrderByDescending(o => o.StageChangedAt ?? o.CreatedAt).Take(500), ct);
        return stages.Select(s => new BoardColumn(s, rows.Where(r => r.StageId == s.Id).ToList())).ToList();
    }

    private async Task<IQueryable<Opportunity>> Filter(string? q, Guid? stageId, string? outcome, bool mine, Guid? stormId)
    {
        var opps = await scope.OpportunitiesAsync();
        if (stormId is { } storm) opps = opps.Where(o => o.StormEventId == storm);
        if (stageId is { } sid) opps = opps.Where(o => o.StageId == sid);
        if (!string.IsNullOrWhiteSpace(outcome)) opps = opps.Where(o => o.Outcome == outcome);
        if (mine) opps = opps.Where(o => o.OwnerUserId == scope.Me);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            opps = opps.Where(o => o.Number.Contains(t) || o.Name.Contains(t)
                                   || db.Contacts.Any(c => c.Id == o.PrimaryContactId && c.DisplayName.Contains(t))
                                   || db.Properties.Any(p => p.Id == o.PropertyId && (p.Line1.Contains(t) || p.City.Contains(t))));
        }
        return opps;
    }

    private async Task<List<OpportunityRow>> Rows(IQueryable<Opportunity> opps, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var raw = await (from o in opps
                         join s in db.PipelineStages on o.StageId equals s.Id
                         join c in db.Contacts on o.PrimaryContactId equals c.Id
                         join p in db.Properties on o.PropertyId equals p.Id into ps from p in ps.DefaultIfEmpty()
                         join u in db.Users on o.OwnerUserId equals u.Id into us from u in us.DefaultIfEmpty()
                         select new
                         {
                             o, StageName = s.Name, s.Color, s.StaleAfterHours, ContactName = c.DisplayName, p,
                             Owner = u == null ? null : u.FirstName + " " + u.LastName,
                         }).ToListAsync(ct);
        return raw.Select(x => new OpportunityRow(x.o.Id, x.o.Number, x.o.Name, x.o.StageId, x.StageName, x.Color, x.o.Outcome,
            x.o.PrimaryContactId, x.ContactName, x.o.PropertyId, x.p?.SingleLine, x.Owner, x.o.ExpectedValue, x.o.IsInsurance,
            x.o.CreatedAt, x.o.StageChangedAt,
            x.o.Outcome == Outcomes.Open && x.StaleAfterHours is { } h && (now - (x.o.StageChangedAt ?? x.o.CreatedAt)).TotalHours > h)).ToList();
    }

    public async Task<OpportunityDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var o = await (await scope.OpportunitiesAsync(includeArchived: true)).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new RecordNotFoundException("Opportunity");
        var stages = await db.PipelineStages.OrderBy(s => s.Sort).ToListAsync(ct);
        var contact = await db.Contacts.FirstAsync(c => c.Id == o.PrimaryContactId, ct);
        var property = o.PropertyId is null ? null : await db.Properties.FirstOrDefaultAsync(p => p.Id == o.PropertyId, ct);
        var owner = o.OwnerUserId is null ? null
            : await db.Users.Where(u => u.Id == o.OwnerUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);

        var history = await (from h in db.OpportunityStageChanges where h.OpportunityId == id
                             join to in db.PipelineStages on h.ToStageId equals to.Id
                             join fromStage in db.PipelineStages on h.FromStageId equals fromStage.Id into fs from fromStage in fs.DefaultIfEmpty()
                             join u in db.Users on h.ByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                             orderby h.CreatedAt descending
                             select new StageChangeRow(h.CreatedAt, fromStage == null ? null : fromStage.Name, to.Name,
                                 u == null ? null : u.FirstName + " " + u.LastName, h.Note)).ToListAsync(ct);

        return new OpportunityDetail(o, stages.First(s => s.Id == o.StageId), contact, property, owner,
            stages.Where(s => s.IsActive).ToList(), history, await scope.CanEditAsync(o.OwnerUserId));
    }

    public async Task<Guid> CreateAsync(OpportunityInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.RecordsCreate);
        await scope.EnsureCanSeeAsync(EntityTypes.Contact, input.PrimaryContactId);
        if (input.StormEventId is { } stormId && !await db.StormEvents.AnyAsync(s => s.Id == stormId, ct))
            throw new CrmRuleException("Unknown storm.");
        if (input.PropertyId is { } pid) await scope.EnsureCanSeeAsync(EntityTypes.Property, pid);
        await contacts.EnsureOwnerIsMemberAsync(input.OwnerUserId, ct);

        var stages = await StagesAsync(ct);
        var stage = input.StageId is { } sid
            ? stages.FirstOrDefault(s => s.Id == sid) ?? throw new CrmRuleException("Unknown pipeline stage.")
            : stages.FirstOrDefault(s => s.IsDefault) ?? stages.First(s => s.Outcome == Outcomes.Open);

        var contact = await db.Contacts.FirstAsync(c => c.Id == input.PrimaryContactId, ct);
        var now = clock.UtcNow;
        var o = new Opportunity
        {
            Number = await numbers.NextAsync(NumberedKinds.Opportunity, ct),
            Name = ContactService.Clean(input.Name) ?? $"{contact.DisplayName}{(input.IsInsurance ? " – insurance" : "")}",
            PrimaryContactId = contact.Id,
            PropertyId = input.PropertyId,
            OwnerUserId = input.OwnerUserId, // null = unassigned pool; forms default to the current user
            StageId = stage.Id,
            Outcome = stage.Outcome,
            StageChangedAt = now,
            CreatedByUserId = scope.Me,
        };
        ApplyDetails(o, input);
        if (stage.Outcome != Outcomes.Open) { o.ClosedAt = now; if (stage.Outcome == Outcomes.Won) o.WonAt = now; }
        db.Opportunities.Add(o);
        db.OpportunityStageChanges.Add(new OpportunityStageChange { OpportunityId = o.Id, ToStageId = stage.Id, ByUserId = scope.Me });

        if (o.PropertyId is { } propertyId) await EnsureContactOnPropertyAsync(propertyId, contact.Id, ct);
        await items.LogAsync(EntityTypes.Opportunity, o.Id, "opportunity.created", $"Opportunity {o.Number} created in {stage.Name}", ct);
        await db.SaveChangesAsync(ct);
        await RefreshContactStatusAsync(contact.Id, ct);
        return o.Id;
    }

    public async Task UpdateAsync(Guid id, OpportunityInput input, CancellationToken ct = default)
    {
        var o = await (await scope.OpportunitiesAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Opportunity");
        await scope.EnsureCanEditAsync(o.OwnerUserId);
        if (input.OwnerUserId != o.OwnerUserId)
        {
            await scope.EnsurePermissionAsync(Permissions.RecordsReassign);
            await contacts.EnsureOwnerIsMemberAsync(input.OwnerUserId, ct);
            o.OwnerUserId = input.OwnerUserId;
        }
        o.Name = ContactService.Clean(input.Name) ?? o.Name;
        ApplyDetails(o, input);
        await items.LogAsync(EntityTypes.Opportunity, id, "opportunity.updated", "Details updated", ct);
        await db.SaveChangesAsync(ct);
    }

    private static void ApplyDetails(Opportunity o, OpportunityInput input)
    {
        o.SourceKey = ContactService.Clean(input.SourceKey);
        o.ExpectedValue = input.ExpectedValue is < 0 ? throw new CrmRuleException("Expected value can't be negative.") : input.ExpectedValue;
        o.IsInsurance = input.IsInsurance;
        o.PerilKey = input.IsInsurance ? ContactService.Clean(input.PerilKey) : null;
        o.DateOfLoss = input.IsInsurance ? input.DateOfLoss : null;
        o.StormEventId = input.IsInsurance ? input.StormEventId : null;
        o.Description = ContactService.Clean(input.Description);
    }

    public async Task MoveStageAsync(Guid id, Guid stageId, string? note, string? lostReasonKey = null, CancellationToken ct = default)
    {
        var o = await (await scope.OpportunitiesAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Opportunity");
        await scope.EnsureCanEditAsync(o.OwnerUserId);
        var stage = await db.PipelineStages.FirstOrDefaultAsync(s => s.Id == stageId && s.IsActive, ct)
                    ?? throw new CrmRuleException("Unknown pipeline stage.");
        if (stage.Id == o.StageId) return;
        if (stage.Outcome == Outcomes.Lost && string.IsNullOrWhiteSpace(lostReasonKey) && string.IsNullOrWhiteSpace(o.LostReasonKey))
            throw new CrmRuleException("Choose a reason when marking an opportunity lost.");

        var from = await db.PipelineStages.Where(s => s.Id == o.StageId).Select(s => s.Name).FirstAsync(ct);
        var now = clock.UtcNow;
        db.OpportunityStageChanges.Add(new OpportunityStageChange
        {
            OpportunityId = id, FromStageId = o.StageId, ToStageId = stage.Id, ByUserId = scope.Me, Note = ContactService.Clean(note),
        });
        o.StageId = stage.Id;
        o.StageChangedAt = now;
        o.Outcome = stage.Outcome;
        o.ClosedAt = stage.Outcome == Outcomes.Open ? null : now;
        o.WonAt = stage.Outcome == Outcomes.Won ? now : null;
        o.LostReasonKey = stage.Outcome == Outcomes.Lost ? ContactService.Clean(lostReasonKey) ?? o.LostReasonKey : null;

        await items.LogAsync(EntityTypes.Opportunity, id, "opportunity.stage_changed", $"Moved from {from} to {stage.Name}", ct);
        await db.SaveChangesAsync(ct);
        await RefreshContactStatusAsync(o.PrimaryContactId, ct);
    }

    /// <summary>
    /// Attaches the opportunity to a property once the address is known: links the primary
    /// contact to the property (as owner if new) and rolls the opportunity's notes, files and
    /// activity up to the property.
    /// </summary>
    public async Task AttachPropertyAsync(Guid id, Guid propertyId, CancellationToken ct = default)
    {
        var o = await (await scope.OpportunitiesAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Opportunity");
        await scope.EnsureCanEditAsync(o.OwnerUserId);
        await scope.EnsureCanSeeAsync(EntityTypes.Property, propertyId);
        if (o.PropertyId == propertyId) return;
        if (o.PropertyId is not null) throw new CrmRuleException("This opportunity already has a property.");

        o.PropertyId = propertyId;
        foreach (var claim in await db.Claims.Where(c => c.OpportunityId == id && c.PropertyId == null).ToListAsync(ct))
            claim.PropertyId = propertyId;
        await EnsureContactOnPropertyAsync(propertyId, o.PrimaryContactId, ct);
        await items.PropagateLinksAsync(EntityTypes.Opportunity, id, EntityTypes.Property, propertyId, ct);
        var address = (await db.Properties.FirstAsync(p => p.Id == propertyId, ct)).SingleLine;
        await items.LogAsync(EntityTypes.Opportunity, id, "opportunity.property_attached", $"Attached to {address}", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Creates the property (or reuses an exact match the user can see) and attaches it.</summary>
    public async Task<Guid> AttachNewPropertyAsync(Guid id, PropertyInput input, CancellationToken ct = default)
    {
        Guid propertyId;
        try { propertyId = await properties.CreateAsync(input, ct); }
        catch (DuplicatePropertyException dup)
        {
            if (!await scope.CanSeeAsync(EntityTypes.Property, dup.ExistingId)) throw;
            propertyId = dup.ExistingId;
        }
        await AttachPropertyAsync(id, propertyId, ct);
        return propertyId;
    }

    public async Task RecordContactAttemptAsync(Guid id, string? note, CancellationToken ct = default)
    {
        var o = await (await scope.OpportunitiesAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Opportunity");
        o.ContactAttempts++;
        o.LastContactAt = clock.UtcNow;
        await items.LogAsync(EntityTypes.Opportunity, id, "opportunity.contact_attempt",
            $"Contact attempt #{o.ContactAttempts}{(string.IsNullOrWhiteSpace(note) ? "" : ": " + note.Trim())}", ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.RecordsDelete);
        var o = await (await scope.OpportunitiesAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Opportunity");
        o.ArchivedAt = clock.UtcNow;
        await items.LogAsync(EntityTypes.Opportunity, id, "opportunity.archived", "Archived", ct);
        await db.SaveChangesAsync(ct);
        await RefreshContactStatusAsync(o.PrimaryContactId, ct);
    }

    private async Task EnsureContactOnPropertyAsync(Guid propertyId, Guid contactId, CancellationToken ct)
    {
        if (!await db.PropertyContacts.AnyAsync(pc => pc.PropertyId == propertyId && pc.ContactId == contactId && pc.EndedOn == null, ct))
            await properties.AddLinkAsync(propertyId, contactId, "owner", false, ct);
    }

    /// <summary>Lead until something is won; customer once anything is won.</summary>
    private async Task RefreshContactStatusAsync(Guid contactId, CancellationToken ct)
    {
        var won = await db.Opportunities.AnyAsync(o => o.PrimaryContactId == contactId && o.Outcome == Outcomes.Won && o.ArchivedAt == null, ct);
        var status = won ? ContactStatuses.Customer : ContactStatuses.Lead;
        var contact = await db.Contacts.FirstAsync(c => c.Id == contactId, ct);
        if (contact.Status == status || contact.Status == ContactStatuses.PastCustomer) return;
        contact.Status = status;
        await db.SaveChangesAsync(ct);
    }
}
