using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using Oes.Application.Abstractions;
using Oes.Infrastructure.Ai;

namespace Oes.UnitTests;

public class StreamAccumulatorTests
{
    private static async Task<(AiResponse Response, List<AiStreamDelta> Deltas)> Run(params string[] events)
    {
        var acc = new StreamAccumulator("claude-opus-5-5");
        var deltas = new List<AiStreamDelta>();
        foreach (var json in events)
            await acc.AddAsync(JsonSerializer.Deserialize<BetaRawMessageStreamEvent>(json)!, d => { deltas.Add(d); return Task.CompletedTask; });
        return (acc.Build(), deltas);
    }

    private const string Start = """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":100,"output_tokens":1,"cache_read_input_tokens":50,"cache_creation_input_tokens":10}}}""";
    private static string BlockStart(int i, string block) => $$"""{"type":"content_block_start","index":{{i}},"content_block":{{block}}}""";
    private static string Delta(int i, string delta) => $$"""{"type":"content_block_delta","index":{{i}},"delta":{{delta}}}""";
    private static string BlockStop(int i) => $$"""{"type":"content_block_stop","index":{{i}}}""";
    private static string MessageDelta(string stop, int output) =>
        "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"" + stop + "\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":" + output + "}}";
    private const string Stop = """{"type":"message_stop"}""";

    [Fact]
    public async Task Thinking_text_and_tool_calls_are_rebuilt_and_text_is_reported_as_it_arrives()
    {
        var (r, deltas) = await Run(
            Start,
            BlockStart(0, """{"type":"thinking","thinking":"","signature":""}"""),
            Delta(0, """{"type":"thinking_delta","thinking":"Let me "}"""),
            Delta(0, """{"type":"thinking_delta","thinking":"look."}"""),
            Delta(0, """{"type":"signature_delta","signature":"sig-abc"}"""),
            BlockStop(0),
            BlockStart(1, """{"type":"text","text":""}"""),
            Delta(1, """{"type":"text_delta","text":"Checking "}"""),
            Delta(1, """{"type":"text_delta","text":"now."}"""),
            BlockStop(1),
            BlockStart(2, """{"type":"tool_use","id":"toolu_1","name":"search_records","input":{}}"""),
            Delta(2, """{"type":"input_json_delta","partial_json":"{\"query\":"}"""),
            Delta(2, """{"type":"input_json_delta","partial_json":"\"Ann\"}"}"""),
            BlockStop(2),
            MessageDelta("tool_use", 42),
            Stop);

        Assert.Equal("tool_use", r.StopReason);
        Assert.Equal(new AiUsage(100, 42, 50, 10), r.Usage);
        Assert.Equal("claude-opus-5-5", r.Model);
        Assert.Equal(new AiThinking("Let me look.", "sig-abc"), r.Content[0]);
        Assert.Equal(new AiText("Checking now."), r.Content[1]);
        var tool = Assert.IsType<AiToolUse>(r.Content[2]);
        Assert.Equal(("toolu_1", "search_records", "Ann"), (tool.Id, tool.Name, tool.Input.GetProperty("query").GetString()));
        Assert.Equal([AiDeltaKinds.Thinking, AiDeltaKinds.TextStart, AiDeltaKinds.Text, AiDeltaKinds.Text], deltas.Select(d => d.Kind));
        Assert.Equal("Checking now.", string.Concat(deltas.Select(d => d.Text)));
    }

    [Fact]
    public void Stop_reasons_and_model_names_read_as_plain_text_from_the_sdk()
    {
        var message = JsonSerializer.Deserialize<BetaMessage>("""{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],"stop_reason":"tool_use","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}""")!;
        Assert.Equal("tool_use", AnthropicAiClient.WireName(message.StopReason!.ToString()!)); // the SDK's ToString keeps the JSON quotes ("\"tool_use\"")
        Assert.Equal("claude-opus-5-5", AnthropicAiClient.ModelName(message.Model.ToString()));
        Assert.Equal("tool_use", AnthropicAiClient.WireName("ToolUse"));
        Assert.Equal("max_tokens", AnthropicAiClient.WireName("\"max_tokens\""));
    }

    [Fact]
    public async Task After_a_mid_stream_fallback_only_text_survives_from_before_the_boundary()
    {
        const string fallback = """{"type":"fallback","from":{"model":"claude-opus-5-5"},"to":{"model":"claude-opus-4-8"},"trigger":{"type":"refusal","category":null}}""";
        var (r, _) = await Run(
            Start,
            BlockStart(0, """{"type":"thinking","thinking":"","signature":""}"""), Delta(0, """{"type":"signature_delta","signature":"sig-1"}"""), BlockStop(0),
            BlockStart(1, """{"type":"text","text":"Partial "}"""), BlockStop(1),
            BlockStart(2, fallback), BlockStop(2),
            BlockStart(3, """{"type":"thinking","thinking":"","signature":""}"""), Delta(3, """{"type":"signature_delta","signature":"sig-2"}"""), BlockStop(3),
            BlockStart(4, """{"type":"text","text":""}"""), Delta(4, """{"type":"text_delta","text":"answer."}"""), BlockStop(4),
            MessageDelta("end_turn", 9), Stop);

        Assert.Equal(["AiText", "AiFallback", "AiThinking", "AiText"], r.Content.Select(b => b.GetType().Name));
        Assert.Equal("sig-2", ((AiThinking)r.Content[2]).Signature);
        // The boundary goes back to the API exactly as it came.
        var f = (AiFallback)r.Content[1];
        var back = JsonSerializer.SerializeToElement(AnthropicAiClient.FallbackParam(f));
        Assert.Equal("fallback", back.GetProperty("type").GetString());
        Assert.Equal("claude-opus-4-8", back.GetProperty("to").GetProperty("model").GetString());
        Assert.Equal(f.Json, BlockJson.Read(BlockJson.Write([f])).OfType<AiFallback>().Single().Json); // and is stored unchanged
    }
}
