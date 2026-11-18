using Oes.Domain.Common;

namespace Oes.Domain.Ai;

/// <summary>A conversation with the assistant. Private to the user who started it.</summary>
public class AiConversation : TenantEntity
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = "New conversation";
    /// <summary>The record it was started from ("Ask about this opportunity"), if any.</summary>
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string Model { get; set; } = "";
    public decimal CostUsd { get; set; }
    public DateTime LastMessageAt { get; set; }
}

/// <summary>One message in the transcript, stored exactly as sent and received (blocks as JSON) so it can be replayed unchanged.</summary>
public class AiChatMessage : TenantEntity
{
    public Guid ConversationId { get; set; }
    public int Seq { get; set; }
    public string Role { get; set; } = "user";
    public string BlocksJson { get; set; } = "[]";
    /// <summary>The visible text, for display and search (no thinking, no tool plumbing).</summary>
    public string? Text { get; set; }
}

/// <summary>Every tool the assistant ran, with its input and a short summary of the output.</summary>
public class AiToolCall : TenantEntity
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public string Tool { get; set; } = "";
    public string InputJson { get; set; } = "{}";
    public string? OutputSummary { get; set; }
    /// <summary>ok / denied (no access or not found) / error.</summary>
    public string Status { get; set; } = "ok";
}

/// <summary>Spend per user per day, checked against budgets before every model call.</summary>
public class AiUsageDaily : TenantEntity
{
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public int Requests { get; set; }
    public decimal CostUsd { get; set; }
}
