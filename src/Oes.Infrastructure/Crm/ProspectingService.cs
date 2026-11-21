using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Communication;
using Oes.Infrastructure.Persistence;
using Oes.Infrastructure.Work;

namespace Oes.Infrastructure.Crm;

/// <summary>Which leads to call. Mine = only leads the user owns; otherwise everything they can see.</summary>
public sealed record ProspectFilter(Guid? StageId = null, string? SourceKey = null, Guid? StormId = null, bool Mine = false, Guid? CampaignId = null);

public sealed record CallRow(DateTime At, string Disposition, string Color, string? By, string? Note);

public sealed record ProspectCard(
    Opportunity Opportunity, PipelineStage Stage, Contact Contact, List<ContactMethod> Methods, Property? Property,
    string? OwnerName, string? StormName, Disposition? LastDisposition, List<CallRow> Calls, string? LockedBy, bool CanEdit);

/// <summary>
/// The prospecting call list: open leads in "prospecting" stages that are due (never called, or
/// their call-back time has come), oldest first, with never-called leads ahead of call-backs.
/// A lead being worked is locked to that person for a few minutes so two people don't call the
/// same homeowner; dispositions record the outcome and decide when the lead comes back.
/// </summary>
public sealed class ProspectingService(
    OesDbContext db, RecordScope scope, IClock clock, TenantTime time, RecordItems items, OpportunityService opportunities,
    ConsentService consents, CampaignQuery campaigns)
{
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(15);

    public Task<List<Disposition>> DispositionsAsync(CancellationToken ct = default) =>
        db.Dispositions.Where(d => d.IsActive).OrderBy(d => d.Sort).ToListAsync(ct);

    public Task<List<PipelineStage>> ProspectingStagesAsync(CancellationToken ct = default) =>
        db.PipelineStages.Where(s => s.IsActive && s.InProspecting && s.Outcome == Outcomes.Open).OrderBy(s => s.Sort).ToListAsync(ct);

    private async Task<IQueryable<Opportunity>> DueAsync(ProspectFilter f)
    {
        var now = clock.UtcNow;
        // A campaign chooses its own leads, whatever their stage.
        var anyStage = f.CampaignId is not null;
        var q = (await scope.OpportunitiesAsync()).Where(o => o.Outcome == Outcomes.Open
            && (o.RecycleAt == null || o.RecycleAt <= now)
            && (anyStage || db.PipelineStages.Any(s => s.Id == o.StageId && s.InProspecting))
            && !db.Dispositions.Any(d => d.Id == o.LastDispositionId && d.IsDoNotContact)
            && db.Contacts.Any(c => c.Id == o.PrimaryContactId && c.ArchivedAt == null));
        return await Filtered(q, f);
    }

    public sealed record QueueCounts(int Due, int BeingWorked, int CallBacksLater);

    public async Task<QueueCounts> CountsAsync(ProspectFilter f, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsProspect);
        var due = await DueAsync(f);
        var cutoff = clock.UtcNow - LockFor;
        var me = scope.Me;
        return new QueueCounts(
            await due.CountAsync(ct),
            await due.CountAsync(o => o.LockedByUserId != null && o.LockedByUserId != me && o.LockedAt >= cutoff, ct),
            await LaterAsync(f, ct));
    }

    private async Task<int> LaterAsync(ProspectFilter f, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var anyStage = f.CampaignId is not null;
        var q = (await scope.OpportunitiesAsync()).Where(o => o.Outcome == Outcomes.Open && o.RecycleAt > now
            && (anyStage || db.PipelineStages.Any(s => s.Id == o.StageId && s.InProspecting))
            && !db.Dispositions.Any(d => d.Id == o.LastDispositionId && d.IsDoNotContact));
        return await (await Filtered(q, f)).CountAsync(ct);
    }

    private async Task<IQueryable<Opportunity>> Filtered(IQueryable<Opportunity> q, ProspectFilter f)
    {
        if (f.CampaignId is { } campaign) q = await campaigns.RestrictAsync(q, campaign);
        if (f.StageId is { } stage) q = q.Where(o => o.StageId == stage);
        if (!string.IsNullOrWhiteSpace(f.SourceKey)) q = q.Where(o => o.SourceKey == f.SourceKey);
        if (f.StormId is { } storm) q = q.Where(o => o.StormEventId == storm);
        if (f.Mine) { var me = scope.Me; q = q.Where(o => o.OwnerUserId == me); }
        return q;
    }

    /// <summary>Claims the next due lead nobody else is working (skipping <paramref name="skip"/>), or null when the list is done.</summary>
    public async Task<Guid?> NextAsync(ProspectFilter f, IReadOnlyCollection<Guid> skip, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsProspect);
        var cutoff = clock.UtcNow - LockFor;
        var me = scope.Me;
        var skipped = skip.ToList();
        var candidates = await (await DueAsync(f))
            .Where(o => !skipped.Contains(o.Id) && (o.LockedByUserId == null || o.LockedByUserId == me || o.LockedAt < cutoff))
            .OrderBy(o => o.ContactAttempts > 0).ThenBy(o => o.RecycleAt ?? o.CreatedAt).ThenBy(o => o.Id)
            .Select(o => o.Id).Take(20).ToListAsync(ct);
        foreach (var id in candidates)
            if (await TryLockAsync(id, ct)) return id;
        return null;
    }

    /// <summary>
    /// Locks the lead to the current user unless someone else holds a live lock; the user's other
    /// locks are released (one lead at a time). Conditional update, so two people can't both win.
    /// </summary>
    public async Task<bool> TryLockAsync(Guid id, CancellationToken ct = default)
    {
        var o = await (await scope.OpportunitiesAsync()).Where(x => x.Id == id).Select(x => new { x.OwnerUserId, x.Outcome }).FirstOrDefaultAsync(ct)
                ?? throw new RecordNotFoundException("Opportunity");
        if (o.Outcome != Outcomes.Open || !await scope.CanEditAsync(o.OwnerUserId)) return false;
        var now = clock.UtcNow;
        var cutoff = now - LockFor;
        var me = scope.Me;
        var won = await db.Opportunities
            .Where(x => x.Id == id && (x.LockedByUserId == null || x.LockedByUserId == me || x.LockedAt < cutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedByUserId, me).SetProperty(x => x.LockedAt, now), ct) == 1;
        if (won)
            await db.Opportunities.Where(x => x.LockedByUserId == me && x.Id != id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedByUserId, (Guid?)null).SetProperty(x => x.LockedAt, (DateTime?)null), ct);
        return won;
    }

    public async Task ReleaseAsync(Guid id, CancellationToken ct = default)
    {
        var me = scope.Me;
        await db.Opportunities.Where(x => x.Id == id && x.LockedByUserId == me)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedByUserId, (Guid?)null).SetProperty(x => x.LockedAt, (DateTime?)null), ct);
    }

    /// <summary>The lead before <paramref name="current"/> in this user's own call history ("Back").</summary>
    public async Task<Guid?> PreviousAsync(Guid? current, CancellationToken ct = default)
    {
        var me = scope.Me;
        var recent = (await db.OpportunityDispositions.Where(d => d.ByUserId == me).OrderByDescending(d => d.CreatedAt)
            .Select(d => d.OpportunityId).Take(200).ToListAsync(ct)).Distinct().ToList();
        var visible = await (await scope.OpportunitiesAsync()).Where(o => recent.Contains(o.Id)).Select(o => o.Id).ToListAsync(ct);
        recent = recent.Where(visible.Contains).ToList();
        var i = current is { } c ? recent.IndexOf(c) : -1;
        return i + 1 < recent.Count ? recent[i + 1] : null;
    }

    public async Task<ProspectCard> CardAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsProspect);
        var o = await (await scope.OpportunitiesAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Opportunity");
        var stage = await db.PipelineStages.FirstAsync(s => s.Id == o.StageId, ct);
        var contact = await db.Contacts.FirstAsync(c => c.Id == o.PrimaryContactId, ct);
        var methods = await db.ContactMethods.Where(m => m.ContactId == contact.Id)
            .OrderByDescending(m => m.Kind == ContactMethodKinds.Mobile || m.Kind == ContactMethodKinds.Phone).ThenByDescending(m => m.IsPrimary)
            .ToListAsync(ct);
        var property = o.PropertyId is null ? null : await db.Properties.FirstOrDefaultAsync(p => p.Id == o.PropertyId, ct);
        var owner = o.OwnerUserId is null ? null
            : await db.Users.Where(u => u.Id == o.OwnerUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);
        var storm = o.StormEventId is null ? null : await db.StormEvents.Where(s => s.Id == o.StormEventId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var last = o.LastDispositionId is null ? null : await db.Dispositions.FirstOrDefaultAsync(d => d.Id == o.LastDispositionId, ct);
        var calls = await (from h in db.OpportunityDispositions where h.OpportunityId == id
                           join d in db.Dispositions on h.DispositionId equals d.Id
                           join u in db.Users on h.ByUserId equals u.Id into us from u in us.DefaultIfEmpty()
                           orderby h.CreatedAt descending
                           select new CallRow(h.CreatedAt, d.Name, d.Color, u == null ? null : u.FirstName + " " + u.LastName, h.Note))
            .Take(10).ToListAsync(ct);
        string? lockedBy = null;
        if (o.LockedByUserId is { } by && by != scope.Me && o.LockedAt >= clock.UtcNow - LockFor)
            lockedBy = await db.Users.Where(u => u.Id == by).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct) ?? "Someone";
        return new ProspectCard(o, stage, contact, methods, property, owner, storm, last, calls, lockedBy, await scope.CanEditAsync(o.OwnerUserId));
    }

    /// <summary>
    /// Records a call outcome: counts the attempt, sets when the lead comes back (the chosen
    /// call-back day at 8 am company time, or the disposition's recycle days), opts the contact out
    /// of marketing for "do not contact", releases the lock, and moves the stage if the disposition says so.
    /// </summary>
    public async Task ApplyAsync(Guid id, Guid dispositionId, string? note, DateOnly? callBackOn = null, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.LeadsProspect);
        var o = await (await scope.OpportunitiesAsync()).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Opportunity");
        await scope.EnsureCanEditAsync(o.OwnerUserId);
        if (o.Outcome != Outcomes.Open) throw new CrmRuleException("This opportunity is closed.");
        var now = clock.UtcNow;
        if (o.LockedByUserId is { } by && by != scope.Me && o.LockedAt >= now - LockFor)
        {
            var name = await db.Users.Where(u => u.Id == by).Select(u => u.FirstName).FirstOrDefaultAsync(ct) ?? "Someone";
            throw new CrmRuleException($"{name} is working this lead right now.");
        }
        var d = await db.Dispositions.FirstOrDefaultAsync(x => x.Id == dispositionId && x.IsActive, ct) ?? throw new CrmRuleException("Pick a call outcome.");
        if (callBackOn is { } day && day < await time.TodayAsync()) throw new CrmRuleException("The call-back date can't be in the past.");
        note = ContactService.Clean(note);
        if (note is { Length: > 500 }) note = note[..500];

        if (d.CountsAsAttempt) { o.ContactAttempts++; o.LastContactAt = now; }
        o.LastDispositionId = d.Id;
        o.RecycleAt = callBackOn is { } cb ? await time.ToUtcAsync(cb, new TimeOnly(8, 0))
            : d.RecycleDays is { } days ? now.AddDays(days) : null;
        o.LockedByUserId = null;
        o.LockedAt = null;
        db.OpportunityDispositions.Add(new OpportunityDisposition { OpportunityId = id, DispositionId = d.Id, ByUserId = scope.Me, Note = note });
        if (d.IsDoNotContact)
            foreach (var channel in Channels.All)
                consents.Add(o.PrimaryContactId, channel, MessagePurposes.Marketing, ConsentStatuses.OptedOut, "disposition", d.Name);

        var summary = d.Name + (d.CountsAsAttempt ? $" (attempt #{o.ContactAttempts})" : "")
                      + (callBackOn is { } c2 ? $", call back {c2:MMM d}" : "") + (note is null ? "" : ": " + note);
        await items.LogAsync(EntityTypes.Opportunity, id, "opportunity.disposition", summary, ct);
        await db.SaveChangesAsync(ct);

        if (d.MovesToStageId is { } stageId && stageId != o.StageId && await db.PipelineStages.AnyAsync(s => s.Id == stageId && s.IsActive, ct))
            await opportunities.MoveStageAsync(id, stageId, note ?? d.Name, d.LostReasonKey ?? "other", ct);
    }
}
