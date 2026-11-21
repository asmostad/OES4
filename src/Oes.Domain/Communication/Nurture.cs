using Oes.Domain.Common;

namespace Oes.Domain.Communication;

public static class StepKinds
{
    public const string Email = "email";
    public const string Sms = "sms";
    public const string Task = "task";
    public static readonly string[] All = [Email, Sms, Task];
}

public static class EnrollmentStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Stopped = "stopped";
}

public static class StepRunStatuses
{
    public const string Done = "done";
    public const string Skipped = "skipped";
    public const string Failed = "failed";
}

/// <summary>A nurture workflow: timed emails, texts and tasks for a contact or opportunity.</summary>
public class Sequence : TenantEntity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Transactional (about their job) or marketing. Decides consent rules, the footer and the unsubscribe link.</summary>
    public string Category { get; set; } = MessagePurposes.Marketing;
    public bool IsActive { get; set; } = true;
    /// <summary>Stop when the contact replies by email or text.</summary>
    public bool StopOnReply { get; set; } = true;
    /// <summary>Stop when the opportunity is won or lost.</summary>
    public bool StopWhenClosed { get; set; } = true;
    public List<SequenceStep> Steps { get; set; } = [];
}

public class SequenceStep : TenantEntity
{
    public Guid SequenceId { get; set; }
    public int Sort { get; set; }
    public string Kind { get; set; } = StepKinds.Email;
    /// <summary>Wait after the previous step (or after enrolling, for the first step).</summary>
    public int DelayMinutes { get; set; }
    /// <summary>Email subject, or the task's title.</summary>
    public string? Subject { get; set; }
    /// <summary>Email or text body (plain text with merge fields), or the task's notes.</summary>
    public string? Body { get; set; }
}

public class Enrollment : TenantEntity
{
    public Guid SequenceId { get; set; }
    public Guid ContactId { get; set; }
    public Guid? OpportunityId { get; set; }
    public string Status { get; set; } = EnrollmentStatuses.Active;
    /// <summary>Index (by Sort order) of the next step to run.</summary>
    public int StepIndex { get; set; }
    /// <summary>When the next step is due; null once finished.</summary>
    public DateTime? NextRunAt { get; set; }
    public int Attempts { get; set; }
    public Guid? EnrolledByUserId { get; set; }
    public string? StopReason { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public class EnrollmentStepRun : TenantEntity
{
    public Guid EnrollmentId { get; set; }
    public Guid StepId { get; set; }
    public int StepIndex { get; set; }
    public string Kind { get; set; } = StepKinds.Email;
    public string Status { get; set; } = StepRunStatuses.Done;
    public string? Detail { get; set; }
    public int Attempt { get; set; } = 1;
    /// <summary>The email, text or task it created.</summary>
    public Guid? ItemId { get; set; }
}
