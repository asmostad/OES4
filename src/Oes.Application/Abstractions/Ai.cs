using System.Text.Json;

namespace Oes.Application.Abstractions;

/// <summary>A block of conversation content, independent of the AI vendor's SDK types.</summary>
public abstract record AiBlock;
public sealed record AiText(string Text) : AiBlock;
/// <summary>Model reasoning. Must be sent back unchanged (signature included) on later turns.</summary>
public sealed record AiThinking(string Thinking, string Signature) : AiBlock;
public sealed record AiRedactedThinking(string Data) : AiBlock;
public sealed record AiToolUse(string Id, string Name, JsonElement Input) : AiBlock;
public sealed record AiToolResult(string ToolUseId, string Content, bool IsError) : AiBlock;

public sealed record AiMessage(string Role, IReadOnlyList<AiBlock> Blocks)
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public sealed record AiToolDefinition(string Name, string Description, JsonElement InputSchema);

public sealed record AiRequest(string System, IReadOnlyList<AiMessage> Messages, IReadOnlyList<AiToolDefinition> Tools, int MaxTokens = 16000);

public sealed record AiUsage(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens);

/// <summary>StopReason is the API's: end_turn, tool_use, max_tokens, refusal, pause_turn…</summary>
public sealed record AiResponse(IReadOnlyList<AiBlock> Content, string StopReason, AiUsage Usage, string Model);

/// <summary>Read one document (a PDF) and answer with JSON that matches <see cref="JsonSchema"/> (structured output).</summary>
public sealed record AiExtractRequest(string System, byte[] Pdf, string Instructions, JsonElement JsonSchema, int MaxTokens = 8000);

/// <summary>Json is the model's answer (valid against the schema) or null when it stopped without one (refusal, cut off).</summary>
public sealed record AiExtractResponse(string? Json, string StopReason, AiUsage Usage, string Model);

/// <summary>The model behind the assistant (Anthropic's Claude in production; a scripted fake in tests).</summary>
public interface IAiClient
{
    /// <summary>False when no API key is configured: callers show "not connected" instead of calling.</summary>
    bool IsConfigured { get; }
    string Model { get; }
    Task<AiResponse> CreateAsync(AiRequest request, CancellationToken ct = default);
    Task<AiExtractResponse> ExtractAsync(AiExtractRequest request, CancellationToken ct = default);
}
