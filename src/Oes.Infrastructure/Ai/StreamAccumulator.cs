using System.Text;
using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using Oes.Application.Abstractions;

namespace Oes.Infrastructure.Ai;

/// <summary>
/// Rebuilds a streamed reply into the same <see cref="AiResponse"/> a non-streamed call returns: text and
/// thinking from their deltas (thinking keeps its signature, so it can go back unchanged), tool input from its
/// JSON pieces, the fallback boundary as-is, the stop reason and usage from message_start and message_delta.
/// It reports text to the caller as it arrives.
/// </summary>
public sealed class StreamAccumulator(string defaultModel)
{
    private sealed class Block
    {
        public string Kind = "";
        public readonly StringBuilder Text = new();
        public readonly StringBuilder Json = new();
        public string Signature = "";
        public string Id = "";
        public string Name = "";
        public string? Raw;
        public string? Data;
    }

    private readonly SortedDictionary<long, Block> blocks = [];
    private string? model;
    private string? stop;
    private long inputTokens, outputTokens, cacheRead, cacheWrite;

    public async Task AddAsync(BetaRawMessageStreamEvent e, Func<AiStreamDelta, Task> onDelta)
    {
        if (e.TryPickStart(out var start))
        {
            model = AnthropicAiClient.ModelName(start.Message.Model.ToString());
            var u = start.Message.Usage;
            (inputTokens, outputTokens, cacheRead, cacheWrite) = (u.InputTokens, u.OutputTokens, u.CacheReadInputTokens ?? 0, u.CacheCreationInputTokens ?? 0);
        }
        else if (e.TryPickContentBlockStart(out var bs))
        {
            var b = new Block();
            var cb = bs.ContentBlock;
            if (cb.TryPickBetaText(out var text)) { b.Kind = "text"; b.Text.Append(text.Text); await onDelta(new AiStreamDelta(AiDeltaKinds.TextStart)); if (text.Text.Length > 0) await onDelta(new AiStreamDelta(AiDeltaKinds.Text, text.Text)); }
            else if (cb.TryPickBetaThinking(out var thinking)) { b.Kind = "thinking"; b.Text.Append(thinking.Thinking); b.Signature = thinking.Signature ?? ""; await onDelta(new AiStreamDelta(AiDeltaKinds.Thinking)); }
            else if (cb.TryPickBetaRedactedThinking(out var redacted)) { b.Kind = "redacted"; b.Data = redacted.Data; await onDelta(new AiStreamDelta(AiDeltaKinds.Thinking)); }
            else if (cb.TryPickBetaToolUse(out var tool)) { b.Kind = "tool_use"; b.Id = tool.ID; b.Name = tool.Name; }
            else if (cb.TryPickBetaFallback(out var fallback)) { b.Kind = "fallback"; b.Raw = JsonSerializer.Serialize(fallback.RawData); }
            blocks[bs.Index] = b;
        }
        else if (e.TryPickContentBlockDelta(out var bd) && blocks.TryGetValue(bd.Index, out var block))
        {
            var d = bd.Delta;
            if (d.TryPickText(out var text)) { block.Text.Append(text.Text); await onDelta(new AiStreamDelta(AiDeltaKinds.Text, text.Text)); }
            else if (d.TryPickThinking(out var thinking)) block.Text.Append(thinking.Thinking);
            else if (d.TryPickSignature(out var signature)) block.Signature += signature.Signature;
            else if (d.TryPickInputJson(out var json)) block.Json.Append(json.PartialJson);
        }
        else if (e.TryPickDelta(out var md))
        {
            stop = md.Delta.StopReason?.ToString();
            var u = md.Usage;
            outputTokens = u.OutputTokens;
            if (u.InputTokens is { } i) inputTokens = i;
            if (u.CacheReadInputTokens is { } r) cacheRead = r;
            if (u.CacheCreationInputTokens is { } w) cacheWrite = w;
        }
    }

    public AiResponse Build()
    {
        var content = new List<AiBlock>();
        foreach (var b in blocks.Values)
        {
            switch (b.Kind)
            {
                case "text": content.Add(new AiText(b.Text.ToString())); break;
                case "thinking": content.Add(new AiThinking(b.Text.ToString(), b.Signature)); break;
                case "redacted": content.Add(new AiRedactedThinking(b.Data ?? "")); break;
                case "tool_use":
                    content.Add(new AiToolUse(b.Id, b.Name, JsonSerializer.Deserialize<JsonElement>(b.Json.Length == 0 ? "{}" : b.Json.ToString())));
                    break;
                case "fallback": content.Add(new AiFallback(b.Raw!)); break;
            }
        }
        var m = model ?? defaultModel;
        return new AiResponse(BeforeFallback(content), AnthropicAiClient.WireName(stop ?? "end_turn"),
            new AiUsage(inputTokens, outputTokens, cacheRead, cacheWrite), m.Contains('-') ? m : defaultModel);
    }

    /// <summary>
    /// After a fallback, thinking, redacted thinking and tool calls from before the last fallback block aren't sent
    /// back (the declined model's partial); text and the fallback block itself stay where they were.
    /// </summary>
    public static List<AiBlock> BeforeFallback(List<AiBlock> content)
    {
        var last = content.FindLastIndex(b => b is AiFallback);
        if (last < 0) return content;
        return content.Where((b, i) => i >= last || b is not (AiThinking or AiRedactedThinking or AiToolUse)).ToList();
    }
}
