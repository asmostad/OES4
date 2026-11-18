using Microsoft.EntityFrameworkCore;
using Oes.Application.Abstractions;
using Oes.Application.Security;
using Oes.Domain.Communication;
using Oes.Domain.Crm;
using Oes.Infrastructure.Crm;
using Oes.Infrastructure.Persistence;

namespace Oes.Infrastructure.Communication;

public sealed class SequenceInput
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = MessagePurposes.Marketing;
    public bool IsActive { get; set; } = true;
    public bool StopOnReply { get; set; } = true;
    public bool StopWhenClosed { get; set; } = true;
}

public sealed class StepInput
{
    public string Kind { get; set; } = StepKinds.Email;
    public int DelayDays { get; set; }
    public int DelayHours { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
}

public sealed record SequenceRow(Guid Id, string Name, string Category, bool IsActive, int Steps, int Active, int Completed);
public sealed record EnrollmentRow(Guid Id, Guid SequenceId, string SequenceName, Guid ContactId, string ContactName, Guid? OpportunityId,
    string Status, int StepIndex, int StepCount, DateTime? NextRunAt, string? StopReason, DateTime CreatedAt);

/// <summary>Nurture workflows: build sequences (nurture.manage) and enroll contacts or opportunities (nurture.enroll).</summary>
public sealed class NurtureService(OesDbContext db, RecordScope scope, ICurrentUser user, IClock clock)
{
    public const int MaxSteps = 20;

    // ---------------- Sequences ----------------

    public async Task<List<SequenceRow>> ListAsync(CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.NurtureManage);
        return await db.Sequences.OrderBy(s => s.Name).Select(s => new SequenceRow(s.Id, s.Name, s.Category, s.IsActive, s.Steps.Count,
            db.Enrollments.Count(e => e.SequenceId == s.Id && e.Status == EnrollmentStatuses.Active),
            db.Enrollments.Count(e => e.SequenceId == s.Id && e.Status == EnrollmentStatuses.Completed))).ToListAsync(ct);
    }

    public async Task<Sequence> GetAsync(Guid id, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.NurtureManage);
        var s = await db.Sequences.Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new RecordNotFoundException("Workflow");
        s.Steps = s.Steps.OrderBy(x => x.Sort).ToList();
        return s;
    }

    public async Task<Guid> SaveAsync(Guid? id, SequenceInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.NurtureManage);
        var name = input.Name?.Trim() ?? "";
        if (name.Length is 0 or > 150) throw new CrmRuleException("Give the workflow a name.");
        if (!MessagePurposes.All.Contains(input.Category)) throw new CrmRuleException("Pick a category.");
        if (await db.Sequences.AnyAsync(s => s.Name == name && s.Id != id, ct)) throw new CrmRuleException("There's already a workflow with that name.");
        var s = id is { } existing ? await GetAsync(existing, ct) : db.Sequences.Add(new Sequence()).Entity;
        s.Name = name; s.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim()[..Math.Min(input.Description.Trim().Length, 1000)];
        s.Category = input.Category; s.IsActive = input.IsActive; s.StopOnReply = input.StopOnReply; s.StopWhenClosed = input.StopWhenClosed;
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    private static void Validate(StepInput input)
    {
        if (!StepKinds.All.Contains(input.Kind)) throw new CrmRuleException("Pick email, text or task.");
        if (input.DelayDays is < 0 or > 365 || input.DelayHours is < 0 or > 23) throw new CrmRuleException("Wait 0–365 days and 0–23 hours.");
        var subject = input.Subject?.Trim();
        var body = input.Body?.Trim();
        switch (input.Kind)
        {
            case StepKinds.Email:
                if (string.IsNullOrEmpty(subject) || subject.Length > 300) throw new CrmRuleException("Emails need a subject.");
                if (string.IsNullOrEmpty(body)) throw new CrmRuleException("Write the email.");
                break;
            case StepKinds.Sms:
                if (string.IsNullOrEmpty(body)) throw new CrmRuleException("Write the text.");
                if (body.Length > TemplateService.SmsMaxLength - 40) throw new CrmRuleException("Keep texts under 1,560 characters.");
                break;
            case StepKinds.Task:
                if (string.IsNullOrEmpty(subject) || subject.Length > 300) throw new CrmRuleException("Give the task a title.");
                break;
        }
        var unknown = MergeFields.Unknown(subject).Concat(MergeFields.Unknown(body)).Distinct().ToList();
        if (unknown.Count > 0) throw new CrmRuleException($"Unknown merge field: {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}.");
    }

    private static void Apply(SequenceStep step, StepInput input)
    {
        step.Kind = input.Kind;
        step.DelayMinutes = input.DelayDays * 24 * 60 + input.DelayHours * 60;
        step.Subject = input.Kind == StepKinds.Sms ? null : input.Subject?.Trim();
        step.Body = string.IsNullOrWhiteSpace(input.Body) ? null : input.Body.Trim();
    }

    public async Task AddStepAsync(Guid sequenceId, StepInput input, CancellationToken ct = default)
    {
        var s = await GetAsync(sequenceId, ct);
        if (s.Steps.Count >= MaxSteps) throw new CrmRuleException($"A workflow can have up to {MaxSteps} steps.");
        Validate(input);
        var step = new SequenceStep { SequenceId = s.Id, Sort = (s.Steps.Count == 0 ? 0 : s.Steps.Max(x => x.Sort)) + 10 };
        Apply(step, input);
        db.SequenceSteps.Add(step);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStepAsync(Guid stepId, StepInput input, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.NurtureManage);
        var step = await db.SequenceSteps.FirstOrDefaultAsync(x => x.Id == stepId, ct) ?? throw new RecordNotFoundException("Step");
        Validate(input);
        Apply(step, input);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removing or reordering steps affects people already enrolled: they continue from the same position.</summary>
    public async Task DeleteStepAsync(Guid stepId, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.NurtureManage);
        var step = await db.SequenceSteps.FirstOrDefaultAsync(x => x.Id == stepId, ct) ?? throw new RecordNotFoundException("Step");
        db.SequenceSteps.Remove(step);
        await db.SaveChangesAsync(ct);
    }

    public async Task MoveStepAsync(Guid stepId, int direction, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.NurtureManage);
        var step = await db.SequenceSteps.FirstOrDefaultAsync(x => x.Id == stepId, ct) ?? throw new RecordNotFoundException("Step");
        var all = await db.SequenceSteps.Where(x => x.SequenceId == step.SequenceId).OrderBy(x => x.Sort).ToListAsync(ct);
        var i = all.IndexOf(step);
        var j = i + Math.Sign(direction);
        if (j < 0 || j >= all.Count) return;
        (all[i].Sort, all[j].Sort) = (all[j].Sort, all[i].Sort);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Enrollments ----------------

    public async Task<List<Sequence>> EnrollableAsync(CancellationToken ct = default) =>
        await db.Sequences.Where(s => s.IsActive && s.Steps.Any()).OrderBy(s => s.Name).ToListAsync(ct);

    /// <summary>Enrolls a contact (and optionally the opportunity it's about). The first step runs after its wait.</summary>
    public async Task<Guid> EnrollAsync(Guid sequenceId, Guid contactId, Guid? opportunityId, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.NurtureEnroll);
        await scope.EnsureCanSeeAsync(EntityTypes.Contact, contactId);
        if (opportunityId is { } oid)
        {
            await scope.EnsureCanSeeAsync(EntityTypes.Opportunity, oid);
            var opp = await db.Opportunities.FirstAsync(o => o.Id == oid, ct);
            if (opp.PrimaryContactId != contactId) throw new CrmRuleException("Enroll the opportunity's own contact.");
            if (opp.Outcome != Outcomes.Open) throw new CrmRuleException("This opportunity is closed.");
        }
        var s = await db.Sequences.Include(x => x.Steps).FirstOrDefaultAsync(x => x.Id == sequenceId && x.IsActive, ct) ?? throw new RecordNotFoundException("Workflow");
        var first = s.Steps.OrderBy(x => x.Sort).FirstOrDefault() ?? throw new CrmRuleException("This workflow has no steps yet.");
        if (await db.Enrollments.AnyAsync(e => e.SequenceId == s.Id && e.ContactId == contactId && e.Status == EnrollmentStatuses.Active, ct))
            throw new CrmRuleException("They're already in this workflow.");
        var e = new Enrollment
        {
            SequenceId = s.Id, ContactId = contactId, OpportunityId = opportunityId, EnrolledByUserId = user.UserId,
            NextRunAt = clock.UtcNow.AddMinutes(first.DelayMinutes),
        };
        db.Enrollments.Add(e);
        await db.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task StopAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        await scope.EnsurePermissionAsync(Permissions.NurtureEnroll);
        var e = await db.Enrollments.FirstOrDefaultAsync(x => x.Id == enrollmentId, ct) ?? throw new RecordNotFoundException("Enrollment");
        await scope.EnsureCanSeeAsync(EntityTypes.Contact, e.ContactId);
        if (e.Status != EnrollmentStatuses.Active) return;
        e.Status = EnrollmentStatuses.Stopped; e.StopReason = "Stopped by staff."; e.NextRunAt = null; e.FinishedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Enrollments for a contact or opportunity page (null = for the workflow admin page, by sequence).</summary>
    public async Task<List<EnrollmentRow>> EnrollmentsAsync(Guid? contactId, Guid? opportunityId, Guid? sequenceId, int take = 100, CancellationToken ct = default)
    {
        if (sequenceId is not null) await scope.EnsurePermissionAsync(Permissions.NurtureManage);
        if (contactId is { } c) await scope.EnsureCanSeeAsync(EntityTypes.Contact, c);
        if (opportunityId is { } o) await scope.EnsureCanSeeAsync(EntityTypes.Opportunity, o);
        var q = from e in db.Enrollments
                join s in db.Sequences on e.SequenceId equals s.Id
                join k in db.Contacts on e.ContactId equals k.Id
                select new { e, s.Name, Steps = s.Steps.Count, k.DisplayName };
        if (contactId is { } cid) q = q.Where(x => x.e.ContactId == cid);
        if (opportunityId is { } oid) q = q.Where(x => x.e.OpportunityId == oid);
        if (sequenceId is { } sid) q = q.Where(x => x.e.SequenceId == sid);
        return (await q.OrderByDescending(x => x.e.CreatedAt).Take(take).ToListAsync(ct))
            .Select(x => new EnrollmentRow(x.e.Id, x.e.SequenceId, x.Name, x.e.ContactId, x.DisplayName, x.e.OpportunityId, x.e.Status,
                x.e.StepIndex, x.Steps, x.e.NextRunAt, x.e.StopReason, x.e.CreatedAt)).ToList();
    }
}
