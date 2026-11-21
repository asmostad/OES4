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
/// <summary>
/// Where the API switched to a fallback model after a refusal (server-side fallbacks). Kept exactly where it
/// appeared and sent back as-is (Json is the block's wire form): the API uses its position to check the thinking around it.
/// </summary>
public sealed record AiFallback(string Json) : AiBlock;

/// <summary>Part of a reply as it streams: a text block starting, more text, or the model starting to think.</summary>
public sealed record AiStreamDelta(string Kind, string? Text = null);

public static class AiDeltaKinds
{
    public const string TextStart = "text_start";
    public const string Text = "text";
    public const string Thinking = "thinking";
}

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

    /// <summary>
    /// The same call, streamed: <paramref name="onDelta"/> hears the reply as it's written, and the complete
    /// response comes back at the end exactly as <see cref="CreateAsync"/> would return it. Without real
    /// streaming, it reports each text block whole.
    /// </summary>
    async Task<AiResponse> CreateStreamingAsync(AiRequest request, Func<AiStreamDelta, Task> onDelta, CancellationToken ct = default)
    {
        var response = await CreateAsync(request, ct);
        foreach (var block in response.Content)
        {
            if (block is AiThinking or AiRedactedThinking) await onDelta(new AiStreamDelta(AiDeltaKinds.Thinking));
            if (block is AiText t) { await onDelta(new AiStreamDelta(AiDeltaKinds.TextStart)); await onDelta(new AiStreamDelta(AiDeltaKinds.Text, t.Text)); }
        }
        return response;
    }

    Task<AiExtractResponse> ExtractAsync(AiExtractRequest request, CancellationToken ct = default);
}
