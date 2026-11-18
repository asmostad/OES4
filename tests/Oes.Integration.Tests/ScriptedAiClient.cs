using System.Text.Json;
using Oes.Application.Abstractions;

namespace Oes.Integration.Tests;

/// <summary>A stand-in for Claude: returns scripted responses in order and records every request.</summary>
public sealed class ScriptedAiClient : IAiClient
{
    private readonly Queue<Func<AiRequest, AiResponse>> _script = new();
    public List<AiRequest> Requests { get; } = [];
    public bool IsConfigured { get; set; } = true;
    public string Model => "claude-opus-5-5";

    public void Reset(params Func<AiRequest, AiResponse>[] steps)
    {
        _script.Clear();
        Requests.Clear();
        IsConfigured = true;
        foreach (var s in steps) _script.Enqueue(s);
    }

    public Task<AiResponse> CreateAsync(AiRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        if (_script.Count == 0) throw new InvalidOperationException("The test didn't script this call.");
        return Task.FromResult(_script.Dequeue()(request));
    }

    public static readonly AiUsage SmallUsage = new(1000, 200, 0, 0);

    public static Func<AiRequest, AiResponse> Says(string text, string stop = "end_turn") =>
        _ => new AiResponse([new AiThinking("", "sig-" + Guid.NewGuid().ToString("N")), new AiText(text)], stop, SmallUsage, "claude-opus-5-5");

    public static Func<AiRequest, AiResponse> Calls(string tool, object input) =>
        _ => new AiResponse([new AiThinking("", "sig-tools"), new AiToolUse("toolu_" + Guid.NewGuid().ToString("N")[..12], tool, JsonSerializer.SerializeToElement(input))],
            "tool_use", SmallUsage, "claude-opus-5-5");
}
