using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Options;
using Oes.Application.Abstractions;

namespace Oes.Infrastructure.Ai;

public sealed class AiOptions
{
    public const string Section = "Ai";

    /// <summary>"Anthropic" to call Claude; anything else (default "None") means not connected.</summary>
    public string Provider { get; set; } = "None";
    /// <summary>Platform key. Leave empty to use the ANTHROPIC_API_KEY environment variable. Never commit a key.</summary>
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>low / medium / high / xhigh / max. Claude Opus 5.5 defaults to medium; set explicitly.</summary>
    public string Effort { get; set; } = "medium";
    public decimal DailyUserBudgetUsd { get; set; } = 3m;
    public decimal DailyTenantBudgetUsd { get; set; } = 30m;
    /// <summary>Tool calls the model may make while answering one message.</summary>
    public int MaxToolRounds { get; set; } = 8;
    // Prices per million tokens (Claude Opus 5.5 list price). Cache writes are 1.25x input.
    public decimal InputPerMTok { get; set; } = 4m;
    public decimal OutputPerMTok { get; set; } = 20m;
    public decimal CacheReadPerMTok { get; set; } = 0.20m;
    public decimal CacheWritePerMTok { get; set; } = 5m;

    public decimal Cost(AiUsage u) =>
        (u.InputTokens * InputPerMTok + u.OutputTokens * OutputPerMTok + u.CacheReadTokens * CacheReadPerMTok + u.CacheWriteTokens * CacheWritePerMTok) / 1_000_000m;
}

/// <summary>
/// Claude through the official Anthropic SDK (beta Messages endpoint, for server-side refusal fallback).
/// Thinking is adaptive (always on for Claude Opus 5.5) with effort from settings; the request is cached
/// with top-level automatic prompt caching, so each turn of a conversation re-reads the earlier ones from cache.
/// </summary>
public sealed class AnthropicAiClient(IOptions<AiOptions> options) : IAiClient
{
    private readonly AiOptions _o = options.Value;
    private AnthropicClient? _client;

    public bool IsConfigured => string.Equals(_o.Provider, "Anthropic", StringComparison.OrdinalIgnoreCase)
                                && (!string.IsNullOrWhiteSpace(_o.ApiKey) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")));
    public string Model => _o.Model;

    private AnthropicClient Client => _client ??= string.IsNullOrWhiteSpace(_o.ApiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = _o.ApiKey };

    public async Task<AiResponse> CreateAsync(AiRequest request, CancellationToken ct = default)
    {
        var parameters = new MessageCreateParams
        {
            Model = _o.Model,
            MaxTokens = request.MaxTokens,
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig { Effort = ParseEffort(_o.Effort) },
            CacheControl = new BetaCacheControlEphemeral(),
            System = request.System,
            Tools = request.Tools.Select(ToTool).ToList(),
            Messages = request.Messages.Select(ToParam).ToList(),
        };
        BetaMessage response = await Client.Beta.Messages.Create(parameters, ct);

        var blocks = new List<AiBlock>();
        foreach (var b in response.Content)
        {
            if (b.TryPickText(out var text)) blocks.Add(new AiText(text.Text));
            else if (b.TryPickThinking(out var thinking)) blocks.Add(new AiThinking(thinking.Thinking, thinking.Signature));
            else if (b.TryPickRedactedThinking(out var redacted)) blocks.Add(new AiRedactedThinking(redacted.Data));
            else if (b.TryPickToolUse(out var tool)) blocks.Add(new AiToolUse(tool.ID, tool.Name, JsonSerializer.SerializeToElement(tool.Input)));
        }
        var u = response.Usage;
        var model = response.Model.ToString();
        return new AiResponse(blocks, WireName(response.StopReason?.ToString() ?? "end_turn"),
            new AiUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens ?? 0, u.CacheCreationInputTokens ?? 0),
            model.Contains('-') ? model : _o.Model);
    }

    /// <summary>"tool_use" whether the SDK hands back the wire value or a C# enum name ("ToolUse").</summary>
    public static string WireName(string value) =>
        value.Contains('_') || value.All(c => !char.IsUpper(c))
            ? value.ToLowerInvariant()
            : string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    /// <summary>One call: the PDF as a document block, then the instructions; structured output constrains the answer to the schema.</summary>
    public async Task<AiExtractResponse> ExtractAsync(AiExtractRequest request, CancellationToken ct = default)
    {
        var schema = request.JsonSchema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var parameters = new MessageCreateParams
        {
            Model = _o.Model,
            MaxTokens = request.MaxTokens,
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig { Effort = ParseEffort(_o.Effort), Format = new BetaJsonOutputFormat { Schema = schema } },
            System = request.System,
            Messages =
            [
                new BetaUserMessageParam
                {
                    Content = new List<BetaContentBlockParam>
                    {
                        new BetaRequestDocumentBlock { Source = new BetaBase64PdfSource { Data = Convert.ToBase64String(request.Pdf) } },
                        new BetaTextBlockParam { Text = request.Instructions },
                    },
                },
            ],
        };
        BetaMessage response = await Client.Beta.Messages.Create(parameters, ct);
        var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        var stop = WireName(response.StopReason?.ToString() ?? "end_turn");
        var u = response.Usage;
        var model = response.Model.ToString();
        return new AiExtractResponse(stop is "refusal" or "max_tokens" || text.Length == 0 ? null : text, stop,
            new AiUsage(u.InputTokens, u.OutputTokens, u.CacheReadInputTokens ?? 0, u.CacheCreationInputTokens ?? 0),
            model.Contains('-') ? model : _o.Model);
    }

    private static Effort ParseEffort(string? value) => value?.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Medium,
    };

    private static BetaToolUnion ToTool(AiToolDefinition t)
    {
        var schema = t.InputSchema;
        return new BetaTool
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = new()
            {
                Properties = schema.TryGetProperty("properties", out var props)
                    ? props.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
                    : new Dictionary<string, JsonElement>(),
                Required = schema.TryGetProperty("required", out var req) ? req.EnumerateArray().Select(r => r.GetString()!).ToList() : [],
            },
        };
    }

    private static BetaMessageParam ToParam(AiMessage m)
    {
        var content = new List<BetaContentBlockParam>();
        foreach (var b in m.Blocks)
        {
            switch (b)
            {
                case AiText t: content.Add(new BetaTextBlockParam { Text = t.Text }); break;
                // Thinking goes back exactly as received (preserved thinking: append-only history).
                case AiThinking t: content.Add(new BetaThinkingBlockParam { Thinking = t.Thinking, Signature = t.Signature }); break;
                case AiRedactedThinking r: content.Add(new BetaRedactedThinkingBlockParam { Data = r.Data }); break;
                case AiToolUse u:
                    content.Add(new BetaToolUseBlockParam
                    {
                        ID = u.Id, Name = u.Name,
                        Input = u.Input.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
                    });
                    break;
                case AiToolResult r: content.Add(new BetaToolResultBlockParam { ToolUseID = r.ToolUseId, Content = r.Content, IsError = r.IsError }); break;
            }
        }
        return m.Role == AiMessage.Assistant ? new BetaAssistantMessageParam { Content = content } : new BetaUserMessageParam { Content = content };
    }
}

/// <summary>Used when no provider is configured: never called (callers check IsConfigured first).</summary>
public sealed class NotConfiguredAiClient(IOptions<AiOptions> options) : IAiClient
{
    public bool IsConfigured => false;
    public string Model => options.Value.Model;
    public Task<AiResponse> CreateAsync(AiRequest request, CancellationToken ct = default) =>
        throw new InvalidOperationException("The AI assistant isn't connected.");
    public Task<AiExtractResponse> ExtractAsync(AiExtractRequest request, CancellationToken ct = default) =>
        throw new InvalidOperationException("The AI assistant isn't connected.");
}
