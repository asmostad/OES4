using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Crm;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Crm;

/// <summary>Which open leads a campaign takes in.</summary>
public sealed class CampaignCriteria
{
    public Guid? StageId { get; set; }
    public string? SourceKey { get; set; }
    public Guid? StormId { get; set; }
    /// <summary>5-digit ZIP codes of the lead's property.</summary>
    public List<string> PostalCodes { get; set; } = [];
    public int? MaxAttempts { get; set; }
    public DateOnly? CreatedFrom { get; set; }
    public DateOnly? CreatedTo { get; set; }
    public bool UnassignedOnly { get; set; }
}

public sealed class CampaignInput
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Mode { get; set; } = CampaignModes.Static;
    public bool IsActive { get; set; } = true;
    public CampaignCriteria Criteria { get; set; } = new();
}

public sealed record CampaignStats(int Leads, int DueNow, int Called, int Won, int Lost, int StillOpen);

public sealed record CampaignRow(Campaign Campaign, CampaignStats Stats);

public sealed record CampaignLeadRow(Guid Id, string Number, string ContactName, string? Address, string StageName, string StageColor, string Outcome,
    int Attempts, string? LastOutcome, DateTime? RecycleAt);

public sealed record CampaignDetail(Campaign Campaign, CampaignCriteria Criteria, CampaignStats Stats, List<(string Name, string Color, int Count)> Outcomes,
    List<CampaignLeadRow> Leads, int NewMatches);

/// <summary>
/// Turns a campaign into a lead query: dynamic campaigns apply the filters, static ones are their
/// members. Shared by the call list and the campaign pages (no record scope here; callers add it).
/// </summary>
public sealed class CampaignQuery(OesDbContext db, TenantTime time)
{
    public static CampaignCriteria Read(string json)
    {
        try { return JsonSerializer.Deserialize<CampaignCriteria>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Open, non-archived opportunities matching the filters.</summary>
    public async Task<IQueryable<Opportunity>> MatchingAsync(IQueryable<Opportunity> q, CampaignCriteria c)
    {
        q = q.Where(o => o.Outcome == Outcomes.Open && o.ArchivedAt == null);
        if (c.StageId is { } stage) q = q.Where(o => o.StageId == stage);
        if (!string.IsNullOrWhiteSpace(c.SourceKey)) q = q.Where(o => o.SourceKey == c.SourceKey);
        if (c.StormId is { } storm) q = q.Where(o => o.StormEventId == storm);
        if (c.PostalCodes.Count > 0)
        {
            var zips = c.PostalCodes;
            q = q.Where(o => db.Properties.Any(p => p.Id == o.PropertyId && zips.Contains(p.PostalCode.Substring(0, 5))));
        }
        if (c.MaxAttempts is { } max) q = q.Where(o => o.ContactAttempts <= max);
        if (c.CreatedFrom is { } from) { var start = await time.ToUtcAsync(from, TimeOnly.MinValue); q = q.Where(o => o.CreatedAt >= start); }
        if (c.CreatedTo is { } to) { var end = await time.ToUtcAsync(to.AddDays(1), TimeOnly.MinValue); q = q.Where(o => o.CreatedAt < end); }
        if (c.UnassignedOnly) q = q.Where(o => o.OwnerUserId == null);
        return q;
    }

    /// <summary>Limits <paramref name="q"/> to the campaign's leads.</summary>
    public async Task<IQueryable<Opportunity>> RestrictAsync(IQueryable<Opportunity> q, Guid campaignId)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId) ?? throw new RecordNotFoundException("Campaign");
        if (campaign.Mode == CampaignModes.Dynamic) return await MatchingAsync(q, Read(campaign.CriteriaJson));
        return q.Where(o => db.CampaignMembers.Any(m => m.CampaignId == campaignId && m.OpportunityId == o.Id));
    }
}

/// <summary>
/// Lead campaigns (lead banks), managed with leads.campaigns. Static campaigns are lists built
/// from the filters (and topped up on request), so calls, wins and losses are tracked against a
/// fixed set; dynamic campaigns always take whatever matches now. Reps work a campaign from the
/// prospecting call list, which still applies their own record visibility.
/// </summary>
public sealed class CampaignService(OesDbContext db, RecordScope scope, CampaignQuery query, ProspectingService prospecting, IAuditLog audit)
{
    public async Task<List<Campaign>> ActiveAsync(CancellationToken ct = default) =>
        await db.Campaigns.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(ct);

    public async Task<List<CampaignRow>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsCampaigns);
        var campaigns = await db.Campaigns.Where(c => includeInactive || c.IsActive).OrderByDescending(c => c.IsActive).ThenBy(c => c.Name).ToListAsync(ct);
        var rows = new List<CampaignRow>();
        foreach (var c in campaigns) rows.Add(new CampaignRow(c, await StatsAsync(c, ct)));
        return rows;
    }

    /// <summary>The campaign's leads (open or closed for static lists; open matches for dynamic ones).</summary>
    private async Task<IQueryable<Opportunity>> LeadsAsync(Campaign c)
    {
        var all = await scope.OpportunitiesAsync();
        if (c.Mode == CampaignModes.Dynamic) return await query.MatchingAsync(all, CampaignQuery.Read(c.CriteriaJson));
        var id = c.Id;
        return all.Where(o => db.CampaignMembers.Any(m => m.CampaignId == id && m.OpportunityId == o.Id));
    }

    private async Task<CampaignStats> StatsAsync(Campaign c, CancellationToken ct)
    {
        var leads = await LeadsAsync(c);
        var since = c.CreatedAt;
        var total = await leads.CountAsync(ct);
        var called = await leads.CountAsync(o => db.OpportunityDispositions.Any(d => d.OpportunityId == o.Id && d.CreatedAt >= since), ct);
        var won = await leads.CountAsync(o => o.Outcome == Outcomes.Won, ct);
        var lost = await leads.CountAsync(o => o.Outcome == Outcomes.Lost, ct);
        var due = (await prospecting.CountsAsync(new ProspectFilter(CampaignId: c.Id), ct)).Due;
        return new CampaignStats(total, due, called, won, lost, total - won - lost);
    }

    public async Task<CampaignDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsCampaigns);
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Campaign");
        var leads = await LeadsAsync(c);
        var since = c.CreatedAt;
        var outcomes = await (from d in db.OpportunityDispositions
                              where d.CreatedAt >= since && leads.Any(o => o.Id == d.OpportunityId)
                              join disp in db.Dispositions on d.DispositionId equals disp.Id
                              group disp by new { disp.Name, disp.Color, disp.Sort } into g
                              orderby g.Key.Sort
                              select new { g.Key.Name, g.Key.Color, Count = g.Count() }).ToListAsync(ct);
        var rows = await (from o in leads.OrderBy(o => o.Outcome != Outcomes.Open).ThenBy(o => o.RecycleAt ?? o.CreatedAt).Take(100)
                          join s in db.PipelineStages on o.StageId equals s.Id
                          join ct2 in db.Contacts on o.PrimaryContactId equals ct2.Id
                          join p in db.Properties on o.PropertyId equals p.Id into ps from p in ps.DefaultIfEmpty()
                          join d in db.Dispositions on o.LastDispositionId equals d.Id into ds from d in ds.DefaultIfEmpty()
                          select new { o.Id, o.Number, ct2.DisplayName, p, StageName = s.Name, s.Color, o.Outcome, o.ContactAttempts, Last = d == null ? null : d.Name, o.RecycleAt })
            .ToListAsync(ct);
        var newMatches = c.Mode == CampaignModes.Static ? await (await NewMatchesAsync(c)).CountAsync(ct) : 0;
        return new CampaignDetail(c, CampaignQuery.Read(c.CriteriaJson), await StatsAsync(c, ct),
            outcomes.Select(x => (x.Name, x.Color, x.Count)).ToList(),
            rows.Select(r => new CampaignLeadRow(r.Id, r.Number, r.DisplayName, r.p?.SingleLine, r.StageName, r.Color, r.Outcome, r.ContactAttempts, r.Last, r.RecycleAt)).ToList(),
            newMatches);
    }

    /// <summary>How many open leads the filters match right now (for the edit form).</summary>
    public async Task<int> PreviewAsync(CampaignCriteria criteria, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsCampaigns);
        return await (await query.MatchingAsync(db.Opportunities, Clean(criteria))).CountAsync(ct);
    }

    public async Task<Guid> SaveAsync(CampaignInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsCampaigns);
        var name = input.Name.Trim();
        if (name.Length is 0 or > 100) throw new CrmRuleException("Give the campaign a name (up to 100 characters).");
        if (input.Mode is not (CampaignModes.Static or CampaignModes.Dynamic)) throw new CrmRuleException("Pick a fixed list or live filters.");
        var criteria = Clean(input.Criteria);
        if (criteria.StageId is { } sid && !await db.PipelineStages.AnyAsync(s => s.Id == sid, ct)) throw new CrmRuleException("Unknown pipeline stage.");
        if (criteria.StormId is { } storm && !await db.StormEvents.AnyAsync(s => s.Id == storm, ct)) throw new CrmRuleException("Unknown storm.");
        if (criteria.MaxAttempts is < 0) throw new CrmRuleException("Attempts can't be negative.");
        if (criteria is { CreatedFrom: { } f, CreatedTo: { } t } && f > t) throw new CrmRuleException("The created-from date is after the created-to date.");

        Campaign c;
        var isNew = input.Id is null;
        if (input.Id is { } id)
        {
            c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Campaign");
            if (c.Mode != input.Mode) throw new CrmRuleException("A campaign can't switch between a fixed list and live filters. Start a new one.");
        }
        else
        {
            c = new Campaign { Mode = input.Mode, CreatedByUserId = scope.Me };
            db.Campaigns.Add(c);
        }
        c.Name = name;
        c.Description = ContactService.Clean(input.Description);
        c.CriteriaJson = JsonSerializer.Serialize(criteria);
        c.IsActive = input.IsActive;
        audit.Record(isNew ? "campaign.created" : "campaign.updated", "Campaign", c.Id, $"Campaign {name}");
        await db.SaveChangesAsync(ct);
        if (isNew && c.Mode == CampaignModes.Static) await AddMatchesAsync(c.Id, ct);
        return c.Id;
    }

    private async Task<IQueryable<Opportunity>> NewMatchesAsync(Campaign c)
    {
        var id = c.Id;
        return (await query.MatchingAsync(db.Opportunities, CampaignQuery.Read(c.CriteriaJson)))
            .Where(o => !db.CampaignMembers.Any(m => m.CampaignId == id && m.OpportunityId == o.Id));
    }

    /// <summary>Adds open leads that now match a static campaign's filters. Returns how many were added.</summary>
    public async Task<int> AddMatchesAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsCampaigns);
        var c = await db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Campaign");
        if (c.Mode != CampaignModes.Static) throw new CrmRuleException("Live campaigns always include every match.");
        var ids = await (await NewMatchesAsync(c)).Select(o => o.Id).Take(5000).ToListAsync(ct);
        foreach (var oid in ids) db.CampaignMembers.Add(new CampaignMember { CampaignId = id, OpportunityId = oid });
        await db.SaveChangesAsync(ct);
        return ids.Count;
    }

    public async Task RemoveLeadAsync(Guid id, Guid opportunityId, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsCampaigns);
        await db.CampaignMembers.Where(m => m.CampaignId == id && m.OpportunityId == opportunityId).ExecuteDeleteAsync(ct);
    }

    private static CampaignCriteria Clean(CampaignCriteria c) => new()
    {
        StageId = c.StageId,
        SourceKey = ContactService.Clean(c.SourceKey),
        StormId = c.StormId,
        PostalCodes = c.PostalCodes.SelectMany(z => (z ?? "").Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Select(z => new string(z.Where(char.IsDigit).ToArray())).Where(z => z.Length >= 5).Select(z => z[..5]).Distinct().Take(200).ToList(),
        MaxAttempts = c.MaxAttempts,
        CreatedFrom = c.CreatedFrom,
        CreatedTo = c.CreatedTo,
        UnassignedOnly = c.UnassignedOnly,
    };
}
