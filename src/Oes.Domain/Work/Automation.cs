using Oes.Domain.Common;

namespace Oes.Domain.Work;

public static class AutomationTriggers
{
    /// <summary>An opportunity entered a pipeline stage (including the first stage of a new lead, and won/lost stages).</summary>
    public const string OpportunityStage = "opportunity_stage";
    /// <summary>A project entered a production workflow stage.</summary>
    public const string ProjectStage = "project_stage";
    /// <summary>A trade entered a trade workflow stage.</summary>
    public const string TradeStage = "trade_stage";
    public static readonly string[] All = [OpportunityStage, ProjectStage, TradeStage];
}

public static class AutomationActions
{
    public const string CreateTask = "create_task";
    public const string Enroll = "enroll";
    public const string Notify = "notify";
    public static readonly string[] All = [CreateTask, Enroll, Notify];
}

/// <summary>"When X enters stage S, do A." Runs once per stage change, from the worker.</summary>
public class AutomationRule : TenantEntity
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string Trigger { get; set; } = AutomationTriggers.OpportunityStage;
    public Guid StageId { get; set; }
    public string Action { get; set; } = AutomationActions.CreateTask;
    /// <summary>Task title or notification text (merge fields allowed).</summary>
    public string? Text { get; set; }
    /// <summary>Task due in N days (0 = today); null = no due date.</summary>
    public int? DueInDays { get; set; }
    /// <summary>Who gets the task or notification; null = the record's owner.</summary>
    public Guid? AssigneeUserId { get; set; }
    /// <summary>Nurture workflow to enroll the record's contact in.</summary>
    public Guid? SequenceId { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>One rule applied to one stage change. Unique per (rule, change), so a rule never runs twice for the same move.</summary>
public class AutomationRun : TenantEntity
{
    public Guid RuleId { get; set; }
    public Guid StageChangeId { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public string Status { get; set; } = "done";
    public string? Detail { get; set; }
    public Guid? ItemId { get; set; }
}
